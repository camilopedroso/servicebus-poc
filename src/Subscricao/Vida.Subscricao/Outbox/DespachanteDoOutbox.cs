using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using Vida.Subscricao.Dados;
using Vida.Telemetria;

namespace Vida.Subscricao.Outbox;

/// <summary>
/// Publica no Service Bus o que a análise gravou na tabela Subscricao.OutboxMensagens (polling a cada segundo).
/// Cópia do despachante de Propostas, sem a simulação de indisponibilidade e sem atualizar proposta nenhuma.
/// Garantia: at-least-once. Se publicar e cair antes de marcar a linha, publica de novo na volta,
/// com o mesmo MessageId: o consumer (ou a detecção de duplicatas da fila) precisa lidar com isso.
/// </summary>
public sealed class DespachanteDoOutbox(
    IServiceScopeFactory escopos,
    ServiceBusClient client,
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
                var lidas = await DespacharLoteAsync(stoppingToken);
                while (lidas == TamanhoDoLote)
                {
                    lidas = await DespacharLoteAsync(stoppingToken);
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
    /// <remarks>internal (e não private): os testes de integração rodam um lote sem esperar o timer.</remarks>
    internal async Task<int> DespacharLoteAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDaSubscricao>();

        // A transação segura o lock das linhas lidas até marcar (table hints no SELECT abaixo).
        // Custo: a transação fica aberta durante o envio ao Service Bus. Com lotes pequenos, aceitável.
        await using var transacao = await banco.Database.BeginTransactionAsync(cancellationToken);
        var agora = DateTimeOffset.UtcNow;
        List<MensagemDeSaida> pendentes;
        // Polling a cada segundo: sem isto, cada consulta vazia viraria um trace no App Insights (86 mil por dia).
        using (SuppressInstrumentationScope.Begin())
        {
            // Nos table hints do SELECT:
            // o UPDLOCK reserva as linhas lidas até o CommitAsync da transação (ninguém mais pega as mesmas),
            // o ROWLOCK faz essa reserva valer linha por linha (sem travar a página inteira),
            // e o READPAST faz as outras instâncias do despachante pularem as linhas reservadas em vez de esperar.
            pendentes = await banco.OutboxMensagens
                .FromSql($"""
                    SELECT TOP ({TamanhoDoLote}) * FROM Subscricao.OutboxMensagens WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE PublicadaEm IS NULL AND (ProximaTentativaEm IS NULL OR ProximaTentativaEm <= {agora})
                    ORDER BY Id
                    """)
                .ToListAsync(cancellationToken);
        }
        if (pendentes.Count == 0)
        {
            return 0;
        }

        // Daqui em diante há trabalho de verdade: um span para o lote, com o envio e o UPDATE dentro dele.
        // As mensagens levam o Diagnostic-Id gravado na análise, então Cobrança e Notificação continuam o trace.
        using var atividade = FonteDeAtividades.Fonte.StartActivity("PublicarOutbox");
        atividade?.SetTag("vida.outbox.mensagens", pendentes.Count);

        var gruposPorDestino = pendentes.GroupBy(m => m.Destino);
        foreach (var grupo in gruposPorDestino)
        {
            var mensagens = grupo.ToList();
            try
            {
                var sender = _senders.GetOrAdd(grupo.Key, client.CreateSender);
                // Lista na ordem do Id: o broker preserva a ordem dentro da chamada.
                var remontadas = mensagens.Select(m => m.ParaServiceBus());
                await sender.SendMessagesAsync(remontadas, cancellationToken);
                MarcarPublicadas(mensagens, agora);
            }
            catch (ServiceBusException ex)
            {
                atividade?.SetStatus(ActivityStatusCode.Error, ex.Reason.ToString());
                MarcarFalha(mensagens, agora, ex);
            }
        }

        await banco.SaveChangesAsync(cancellationToken);
        // Commit depois do envio: se cair aqui, as linhas continuam pendentes e saem de novo (duplicata).
        await transacao.CommitAsync(cancellationToken);
        return pendentes.Count;
    }

    private void MarcarPublicadas(List<MensagemDeSaida> mensagens, DateTimeOffset agora)
    {
        foreach (var mensagem in mensagens)
        {
            mensagem.PublicadaEm = agora;
            mensagem.UltimoErro = null;
            var atraso = agora - mensagem.CriadaEm;
            MetricasDeNegocio.AtrasoDoOutbox.Record(atraso.TotalMilliseconds);
            logger.LogInformation(
                "Outbox: mensagem {MessageId} publicada em '{Destino}' ({Atraso:F0} ms depois de gravada, tentativa {Tentativa}).",
                mensagem.MessageId, mensagem.Destino, atraso.TotalMilliseconds, mensagem.Tentativas + 1);
        }
    }

    private void MarcarFalha(List<MensagemDeSaida> mensagens, DateTimeOffset agora, ServiceBusException erro)
    {
        // Backoff por linha: 2, 4, 8 s... até EsperaMaxima. A análise não sente nada: já concluiu a mensagem.
        var espera = TimeSpan.Zero;
        var texto = $"{erro.Reason}: {erro.Message}";
        foreach (var mensagem in mensagens)
        {
            mensagem.Tentativas++;
            var segundos = Math.Pow(2, mensagem.Tentativas);
            var segundosLimitados = Math.Min(segundos, EsperaMaxima.TotalSeconds);
            espera = TimeSpan.FromSeconds(segundosLimitados);
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
