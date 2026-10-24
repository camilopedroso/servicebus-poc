using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Move mensagens da DLQ da fila "propostas" de volta para a fila principal.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class ReprocessamentoDaDlq(ServiceBusClient client, ILogger<ReprocessamentoDaDlq> logger)
{
    /// <summary>
    /// Reprocessa mensagens da DLQ de volta para a fila principal.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Quantas mensagens foram movidas.</returns>
    public async Task<int> ReprocessarAsync(CancellationToken cancellationToken)
    {
        await using var dlqReceiver = client.CreateReceiver(Filas.Propostas, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
        });
        await using var sender = client.CreateSender(Filas.Propostas);

        var total = 0;
        while (true)
        {
            var mensagens = await dlqReceiver.ReceiveMessagesAsync(
                maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(3), cancellationToken);
            if (mensagens.Count == 0) break;

            foreach (var m in mensagens)
            {
                // Copia corpo e propriedades para uma nova mensagem (DeliveryCount volta a 0).
                // Remove o gatilho de falha simulada para a mensagem conseguir ser processada.
                var reenvio = new ServiceBusMessage(m);
                reenvio.ApplicationProperties.Remove(Propriedades.SimularFalha);
                await sender.SendMessageAsync(reenvio, cancellationToken);
                // Só completa na DLQ depois do reenvio: se cair no meio, no pior caso duplica (at-least-once), não perde.
                await dlqReceiver.CompleteMessageAsync(m, cancellationToken);
                logger.LogInformation(
                    "  proposta {PropostaId} devolvida à fila (motivo era {Motivo}).", m.MessageId, m.DeadLetterReason);
                total++;
            }
        }
        logger.LogInformation("{Total} mensagem(ns) movida(s) da DLQ de volta para '{Fila}'.", total, Filas.Propostas);
        return total;
    }
}
