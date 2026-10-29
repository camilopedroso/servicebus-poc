using Azure.Messaging.ServiceBus;
using Vida.Operacao.Contratos;
using Vida.Plataforma.Mensagens;

namespace Vida.Operacao.Ferramentas;

/// <summary>
/// Move mensagens da DLQ de uma fila de volta para a MESMA fila. Para a fila, e não para o tópico que a alimenta:
/// no tópico, a cópia chegaria de novo a todas as assinaturas, não só ao serviço dono da fila.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna da Operação.</param>
public sealed class ReprocessamentoDaDlq(ServiceBusClient client, ILogger<ReprocessamentoDaDlq> logger)
{
    /// <summary>
    /// Reprocessa as mensagens da DLQ de volta para a fila principal.
    /// Duas proteções contra loop (reenviar → morrer de novo → reenviar...):
    /// 1) motivo permanente (payload inválido) fica na DLQ: reenviar daria o mesmo erro;
    /// 2) só entra o que já estava na DLQ no início: uma cópia que morrer de novo fica para a próxima execução.
    /// </summary>
    /// <param name="fila">Nome da fila (como está no broker).</param>
    /// <param name="removerPropriedade">
    /// Application property a tirar de cada cópia antes de reenviar, ou null. Simula "corrigir a causa antes de
    /// reprocessar": o cenário do painel tira o simularFalha. A ferramenta não sabe o que a propriedade significa.
    /// </param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Quantas mensagens foram movidas e quantas ficaram na DLQ.</returns>
    public async Task<ResultadoDoReprocessamento> ReprocessarAsync(
        string fila, string? removerPropriedade, CancellationToken cancellationToken)
    {
        var opcoesDaDlq = new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
        };
        await using var dlqReceiver = client.CreateReceiver(fila, opcoesDaDlq);
        await using var sender = client.CreateSender(fila);

        var ultimaSequencia = await UltimaSequenciaAsync(dlqReceiver, cancellationToken);
        if (ultimaSequencia is null)
        {
            logger.LogInformation("DLQ de '{Fila}' vazia: nada a reprocessar.", fila);
            return new ResultadoDoReprocessamento(Movidas: 0, Mantidas: 0);
        }

        // Ficam travadas (peek-lock) até o fim: se fossem liberadas na hora, o próximo receive as traria de volta.
        var mantidas = new Dictionary<long, ServiceBusReceivedMessage>();
        var movidas = 0;
        var esperaMaxima = TimeSpan.FromSeconds(3);
        try
        {
            while (true)
            {
                var mensagens = await dlqReceiver.ReceiveMessagesAsync(
                    maxMessages: 20, maxWaitTime: esperaMaxima, cancellationToken);
                if (mensagens.Count == 0) break;

                foreach (var m in mensagens)
                {
                    if (m.SequenceNumber > ultimaSequencia)
                    {
                        // Chegou à DLQ depois do início (pode ser uma cópia que acabamos de reenviar).
                        mantidas[m.SequenceNumber] = m;
                        continue;
                    }
                    if (MotivosDeDeadLetter.Permanentes.Contains(m.DeadLetterReason))
                    {
                        if (mantidas.TryAdd(m.SequenceNumber, m))
                        {
                            logger.LogWarning(
                                "  mensagem {MessageId} mantida na DLQ: {Motivo} não se resolve reenviando ({Descricao}).",
                                m.MessageId, m.DeadLetterReason, m.DeadLetterErrorDescription);
                        }
                        continue;
                    }

                    // Copia corpo e propriedades para uma nova mensagem (DeliveryCount volta a 0).
                    var reenvio = new ServiceBusMessage(m);
                    if (removerPropriedade is not null)
                    {
                        reenvio.ApplicationProperties.Remove(removerPropriedade);
                    }
                    await sender.SendMessageAsync(reenvio, cancellationToken);
                    // Só completa na DLQ depois do reenvio: se cair no meio, no pior caso duplica (at-least-once), não perde.
                    await dlqReceiver.CompleteMessageAsync(m, cancellationToken);
                    logger.LogInformation(
                        "  mensagem {MessageId} devolvida à fila (motivo era {Motivo}).", m.MessageId, m.DeadLetterReason);
                    movidas++;
                }
            }
        }
        finally
        {
            // Libera as mantidas: continuam na DLQ, com o mesmo motivo.
            foreach (var m in mantidas.Values)
            {
                await LiberarAsync(dlqReceiver, m);
            }
        }

        logger.LogInformation(
            "{Movidas} mensagem(ns) movida(s) da DLQ de volta para '{Fila}'; {Mantidas} mantida(s) na DLQ.",
            movidas, fila, mantidas.Count);
        return new ResultadoDoReprocessamento(movidas, mantidas.Count);
    }

    /// <summary>
    /// Maior SequenceNumber presente na DLQ agora, lido por peek (sem travar nada). Null se a DLQ estiver vazia.
    /// </summary>
    private static async Task<long?> UltimaSequenciaAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken)
    {
        long? ultima = null;
        while (true)
        {
            // Peek continua de onde parou a cada chamada: lê a DLQ inteira em páginas.
            var pagina = await receiver.PeekMessagesAsync(maxMessages: 250, cancellationToken: cancellationToken);
            if (pagina.Count == 0) return ultima;
            var maiorDaPagina = pagina.Max(m => m.SequenceNumber);
            if (ultima is null || maiorDaPagina > ultima)
            {
                ultima = maiorDaPagina;
            }
        }
    }

    private async Task LiberarAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage mensagem)
    {
        try
        {
            // Sem o token da requisição: mesmo se o painel desconectar, o lock é devolvido.
            await receiver.AbandonMessageAsync(mensagem, cancellationToken: CancellationToken.None);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            // O lock já expirou: a mensagem já voltou a ficar disponível na DLQ.
            logger.LogDebug("Lock da mensagem {MessageId} expirou antes de liberar.", mensagem.MessageId);
        }
    }
}
