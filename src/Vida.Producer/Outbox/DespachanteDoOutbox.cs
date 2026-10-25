using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Vida.Mensageria.Mensagens;
using Vida.Producer.Dados;

namespace Vida.Producer.Outbox;

/// <summary>
/// Publica no Service Bus o que a API gravou na tabela OutboxMensagens (polling a cada segundo).
/// Garantia: at-least-once. Se publicar e cair antes de marcar a linha, publica de novo na volta,
/// com o mesmo MessageId: o consumer (ou a detecção de duplicatas da fila) precisa lidar com isso.
/// </summary>
public sealed class DespachanteDoOutbox(
    IServiceScopeFactory escopos,
    ServiceBusClient client,
    SimulacaoDeIndisponibilidade simulacao,
    ILogger<DespachanteDoOutbox> logger) : BackgroundService
{
    private const int TamanhoDoLote = 20;
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);
        string? ultimoErro = null;
        while (await relogio.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Lote cheio: provavelmente há mais pendentes, então não espera o próximo tique.
                while (await DespacharLoteAsync(stoppingToken) == TamanhoDoLote)
                {
                }
                ultimoErro = null;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Banco fora do ar, por exemplo. Loga uma vez por tipo de erro para não inundar o painel.
                if (ex.Message != ultimoErro)
                {
                    logger.LogError("Outbox: não foi possível ler as pendentes ({Erro}). Tentando a cada segundo.", ex.Message);
                    ultimoErro = ex.Message;
                }
            }
        }
    }

    /// <summary>
    /// Pega até TamanhoDoLote pendentes, publica e marca, tudo dentro de uma transação do banco.
    /// </summary>
    /// <returns>Quantas linhas foram lidas.</returns>
    private async Task<int> DespacharLoteAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();

        // A transação segura o lock das linhas lidas até marcar. UPDLOCK: ninguém mais pega as mesmas;
        // READPAST: outra instância do despachante pula as travadas em vez de esperar, e pega as próximas.
        // Custo: a transação fica aberta durante o envio ao Service Bus. Com lotes pequenos, aceitável.
        await using var transacao = await banco.Database.BeginTransactionAsync(cancellationToken);
        var agora = DateTimeOffset.UtcNow;
        var pendentes = await banco.OutboxMensagens
            .FromSql($"""
                SELECT TOP ({TamanhoDoLote}) * FROM OutboxMensagens WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE PublicadaEm IS NULL AND (ProximaTentativaEm IS NULL OR ProximaTentativaEm <= {agora})
                ORDER BY Id
                """)
            .ToListAsync(cancellationToken);
        if (pendentes.Count == 0)
        {
            return 0;
        }

        foreach (var grupo in pendentes.GroupBy(m => m.Destino))
        {
            var mensagens = grupo.ToList();
            try
            {
                if (simulacao.Ativa)
                {
                    throw new ServiceBusException(
                        "Simulação: Service Bus indisponível.", ServiceBusFailureReason.ServiceCommunicationProblem);
                }
                var sender = _senders.GetOrAdd(grupo.Key, client.CreateSender);
                // Lista na ordem do Id: o broker preserva a ordem dentro da chamada.
                await sender.SendMessagesAsync(mensagens.Select(m => m.ParaServiceBus()), cancellationToken);
                await MarcarPublicadasAsync(banco, mensagens, agora, cancellationToken);
            }
            catch (ServiceBusException ex)
            {
                MarcarFalha(mensagens, agora, ex);
            }
        }

        await banco.SaveChangesAsync(cancellationToken);
        // Commit depois do envio: se cair aqui, as linhas continuam pendentes e saem de novo (duplicata).
        await transacao.CommitAsync(cancellationToken);
        return pendentes.Count;
    }

    private async Task MarcarPublicadasAsync(
        BancoDePropostas banco, List<MensagemDeSaida> mensagens, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        foreach (var mensagem in mensagens)
        {
            mensagem.PublicadaEm = agora;
            mensagem.UltimoErro = null;
            logger.LogInformation(
                "Outbox: mensagem {MessageId} publicada em '{Destino}' ({Atraso:F0} ms depois de gravada, tentativa {Tentativa}).",
                mensagem.MessageId, mensagem.Destino, (agora - mensagem.CriadaEm).TotalMilliseconds, mensagem.Tentativas + 1);
        }

        // Atualiza a proposta junto, na mesma transação. Acopla o despachante às propostas, mas evita outro
        // mecanismo só para o GET mostrar "EnviadaParaAnalise".
        var propostas = mensagens
            .Where(m => m.Subject == TiposDeMensagem.PropostaRecebida)
            .Select(m => Guid.Parse(m.MessageId))
            .ToList();
        if (propostas.Count > 0)
        {
            await banco.Propostas
                .Where(p => propostas.Contains(p.PropostaId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, StatusDaProposta.EnviadaParaAnalise)
                    .SetProperty(p => p.EnviadaEm, agora), cancellationToken);
        }
    }

    private void MarcarFalha(List<MensagemDeSaida> mensagens, DateTimeOffset agora, ServiceBusException erro)
    {
        // Backoff por linha: 2, 4, 8 s... até EsperaMaxima. A API não sente nada: já respondeu 201.
        var espera = TimeSpan.Zero;
        var texto = $"{erro.Reason}: {erro.Message}";
        foreach (var mensagem in mensagens)
        {
            mensagem.Tentativas++;
            espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, mensagem.Tentativas), EsperaMaxima.TotalSeconds));
            mensagem.ProximaTentativaEm = agora + espera;
            mensagem.UltimoErro = texto.Length > 1000 ? texto[..1000] : texto;
        }
        logger.LogWarning(
            "Outbox: {Quantidade} mensagem(ns) para '{Destino}' não publicada(s): {Motivo}. Próxima tentativa em {Espera} s.",
            mensagens.Count, mensagens[0].Destino, erro.Reason, espera.TotalSeconds);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }
}
