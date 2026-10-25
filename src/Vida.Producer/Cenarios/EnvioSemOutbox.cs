using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Dominio;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Dados;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Fase 4, o problema que o outbox resolve ("dual write"): gravar no banco e publicar no Service Bus são duas
/// operações em dois sistemas, sem transação entre eles. Se o processo cair entre as duas, uma acontece e a outra não.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class EnvioSemOutbox(ServiceBusClient client, ILogger<EnvioSemOutbox> logger) : IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(Filas.Propostas);

    /// <summary>
    /// Grava a proposta e publica direto. Com a falha simulada, o "processo cai" depois do INSERT.
    /// </summary>
    /// <param name="banco">DbContext da requisição.</param>
    /// <param name="falharAntesDoEnvio">Se true, interrompe entre o INSERT e o envio.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A proposta gravada e se a mensagem saiu.</returns>
    public async Task<(PropostaRegistrada Proposta, bool Publicada)> EnviarAsync(
        BancoDePropostas banco, bool falharAntesDoEnvio, CancellationToken cancellationToken)
    {
        var proposta = new PropostaSeguroVida(
            Guid.NewGuid(), $"CLI-{Random.Shared.Next(1000, 9999)}", Random.Shared.Next(18, 70),
            Random.Shared.Next(50, 2000) * 1_000m, Planos.Sortear(), DateTimeOffset.UtcNow);
        var registro = new PropostaRegistrada
        {
            PropostaId = proposta.PropostaId,
            ClienteId = proposta.ClienteId,
            Idade = proposta.Idade,
            CapitalSegurado = proposta.CapitalSegurado,
            Plano = proposta.Plano,
            Status = StatusDaProposta.Recebida,
            RecebidaEm = proposta.CriadaEm,
        };

        // 1ª escrita: banco. Commit feito, não volta atrás.
        banco.Propostas.Add(registro);
        await banco.SaveChangesAsync(cancellationToken);

        if (falharAntesDoEnvio)
        {
            // Deploy, OOM, timeout do Service Bus... Na prática, qualquer coisa entre as duas linhas.
            logger.LogError(
                "Sem outbox: proposta {PropostaId} gravada no banco, e o processo \"caiu\" antes de publicar. Ninguém vai analisá-la: fica Recebida para sempre.",
                registro.PropostaId);
            return (registro, false);
        }

        // 2ª escrita: Service Bus. Na ordem inversa (publicar antes de gravar), a falha deixaria uma mensagem
        // de uma proposta que não existe no banco.
        await _sender.SendMessageAsync(MensagensProposta.Criar(proposta), cancellationToken);
        registro.Status = StatusDaProposta.EnviadaParaAnalise;
        registro.EnviadaEm = DateTimeOffset.UtcNow;
        await banco.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Sem outbox: proposta {PropostaId} gravada e publicada (deu certo desta vez).", registro.PropostaId);
        return (registro, true);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
