using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Ferramentas de operação: peek na fila, nas assinaturas e nas DLQs, e leitura das regras aplicadas pelo broker.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="admin">Administration client, para ler as regras.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class InspecaoDeEntidades(
    ServiceBusClient client, ServiceBusAdministrationClient admin, ILogger<InspecaoDeEntidades> logger)
{
    private const int MaximoDeMensagens = 50;

    /// <summary>
    /// Espia mensagens da fila "propostas" ou da DLQ dela.
    /// </summary>
    /// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a fila normal.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Mensagens encontradas (até 50).</returns>
    public async Task<MensagemEspiada[]> EspiarFilaAsync(bool deadLetter, CancellationToken cancellationToken)
    {
        // Peek não trava nem consome: útil para inspeção/suporte, sem afetar consumers.
        await using var receiver = client.CreateReceiver(Filas.Propostas, new ServiceBusReceiverOptions
        {
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
        });

        var mensagens = await receiver.PeekMessagesAsync(MaximoDeMensagens, cancellationToken: cancellationToken);
        var nome = deadLetter ? $"{Filas.Propostas}/$DeadLetterQueue" : Filas.Propostas;
        logger.LogInformation("{Quantidade} mensagem(ns) em '{Entidade}'.", mensagens.Count, nome);

        foreach (var m in mensagens)
        {
            var proposta = m.Body.ToObjectFromJson<PropostaSeguroVida>();
            logger.LogInformation(
                "  #{Seq} proposta {PropostaId} idade={Idade} entregas={Entregas}{Motivo}",
                m.SequenceNumber, m.MessageId, proposta?.Idade, m.DeliveryCount, MotivoDaDlq(m, deadLetter));
        }
        return [.. mensagens.Select(Espiada)];
    }

    /// <summary>
    /// Espia as mensagens de uma assinatura (de qualquer tópico).
    /// </summary>
    /// <param name="assinatura">Nome da assinatura.</param>
    /// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a assinatura.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Mensagens encontradas (até 50).</returns>
    public async Task<MensagemEspiada[]> EspiarAssinaturaAsync(string assinatura, bool deadLetter, CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(Assinaturas.TopicoDe(assinatura), assinatura, new ServiceBusReceiverOptions
        {
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
        });

        var mensagens = await receiver.PeekMessagesAsync(MaximoDeMensagens, cancellationToken: cancellationToken);
        var nome = $"{Assinaturas.TopicoDe(assinatura)}/subscriptions/{assinatura}{(deadLetter ? "/$DeadLetterQueue" : "")}";
        logger.LogInformation("{Quantidade} mensagem(ns) em '{Entidade}'.", mensagens.Count, nome);

        foreach (var m in mensagens)
        {
            // Mostra as propriedades: é o que os filtros enxergam (a action da regra pode ter adicionado alguma).
            var propriedades = string.Join(", ", m.ApplicationProperties.Select(p => $"{p.Key}={p.Value}"));
            logger.LogInformation(
                "  #{Seq} {Subject} {MessageId} [{Propriedades}] entregas={Entregas}{Motivo}",
                m.SequenceNumber, m.Subject, m.MessageId, propriedades, m.DeliveryCount, MotivoDaDlq(m, deadLetter));
        }
        return [.. mensagens.Select(Espiada)];
    }

    /// <summary>
    /// Lista as regras de todas as assinaturas, dos dois tópicos.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Regras por assinatura.</returns>
    public async Task<RegrasDaAssinatura[]> ListarRegrasAsync(CancellationToken cancellationToken)
    {
        // Administration client = operações de gerenciamento (criar/listar entidades e regras) pelo data plane.
        // A infra de verdade está no Bicep; aqui é só leitura, para conferir o que o broker aplicou.
        var resultado = new List<RegrasDaAssinatura>();
        foreach (var assinatura in Assinaturas.Todas)
        {
            logger.LogInformation("{Topico}/{Assinatura}:", Assinaturas.TopicoDe(assinatura), assinatura);
            var regras = new List<string>();
            await foreach (var regra in admin.GetRulesAsync(Assinaturas.TopicoDe(assinatura), assinatura, cancellationToken))
            {
                var acao = DescricaoDeRegras.DescreverAcao(regra) is { } a ? $" | action: {a}" : "";
                var linha = $"regra {regra.Name}: {DescricaoDeRegras.DescreverFiltro(regra)}{acao}";
                logger.LogInformation("  {Regra}", linha);
                regras.Add(linha);
            }
            resultado.Add(new RegrasDaAssinatura(assinatura, [.. regras]));
        }
        return [.. resultado];
    }

    private static string MotivoDaDlq(ServiceBusReceivedMessage m, bool deadLetter) =>
        deadLetter ? $" motivo: {m.DeadLetterReason} | {m.DeadLetterErrorDescription}" : "";

    private static MensagemEspiada Espiada(ServiceBusReceivedMessage m) => new(
        m.SequenceNumber,
        m.MessageId,
        m.Subject,
        m.DeliveryCount,
        m.ApplicationProperties,
        m.DeadLetterReason,
        m.DeadLetterErrorDescription);
}
