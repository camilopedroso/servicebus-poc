using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Vida.Operacao.Contratos;
using Vida.Plataforma.Topologia;

namespace Vida.Operacao.Ferramentas;

/// <summary>
/// Peek em filas, assinaturas e DLQs, e leitura das regras aplicadas pelo broker. Não conhece nenhum contrato:
/// mostra o que qualquer mensagem tem (Subject, MessageId, propriedades e o começo do corpo).
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="admin">Administration client, para ler as regras.</param>
/// <param name="descoberta">Lista de assinaturas que existem no broker.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna da Operação.</param>
public sealed class InspecaoDeEntidades(
    ServiceBusClient client, ServiceBusAdministrationClient admin, DescobertaDeEntidades descoberta,
    ILogger<InspecaoDeEntidades> logger)
{
    private const int MaximoDeMensagens = 50;
    private const int TamanhoDoResumoDoCorpo = 80;

    /// <summary>
    /// Espia as mensagens de uma fila ou da DLQ dela.
    /// </summary>
    /// <param name="fila">Nome da fila (como está no broker).</param>
    /// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a fila normal.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Mensagens encontradas (até 50).</returns>
    public async Task<MensagemEspiada[]> EspiarFilaAsync(string fila, bool deadLetter, CancellationToken cancellationToken)
    {
        // Peek não trava nem consome: útil para inspeção/suporte, sem afetar consumers.
        var opcoes = new ServiceBusReceiverOptions
        {
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
        };
        await using var receiver = client.CreateReceiver(fila, opcoes);
        var sufixoDaDlq = deadLetter ? "/$DeadLetterQueue" : "";
        return await EspiarAsync(receiver, $"{fila}{sufixoDaDlq}", deadLetter, cancellationToken);
    }

    /// <summary>
    /// Espia as mensagens de uma assinatura ou da DLQ dela.
    /// </summary>
    /// <param name="topico">Tópico da assinatura.</param>
    /// <param name="assinatura">Nome da assinatura.</param>
    /// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a assinatura.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Mensagens encontradas (até 50).</returns>
    public async Task<MensagemEspiada[]> EspiarAssinaturaAsync(
        string topico, string assinatura, bool deadLetter, CancellationToken cancellationToken)
    {
        var opcoes = new ServiceBusReceiverOptions
        {
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
        };
        await using var receiver = client.CreateReceiver(topico, assinatura, opcoes);
        var sufixoDaDlq = deadLetter ? "/$DeadLetterQueue" : "";
        return await EspiarAsync(receiver, $"{topico}/subscriptions/{assinatura}{sufixoDaDlq}", deadLetter, cancellationToken);
    }

    /// <summary>
    /// Lista as regras de todas as assinaturas que existem no broker, inclusive as que só encaminham (forwardTo).
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Regras por assinatura.</returns>
    public async Task<RegrasDaAssinatura[]> ListarRegrasAsync(CancellationToken cancellationToken)
    {
        // A infra de verdade está no Bicep de cada squad; aqui é só leitura, para conferir o que o broker aplicou.
        var resultado = new List<RegrasDaAssinatura>();
        var assinaturas = await descoberta.ListarAssinaturasAsync(cancellationToken);
        foreach (var assinatura in assinaturas)
        {
            var encaminhamento = assinatura.EncaminhaPara is null ? "" : $" → forwardTo {assinatura.EncaminhaPara}";
            logger.LogInformation("{Topico}/{Assinatura}{Encaminhamento}:", assinatura.Topico, assinatura.Nome, encaminhamento);
            var regras = new List<string>();
            var regrasDoBroker = admin.GetRulesAsync(assinatura.Topico, assinatura.Nome, cancellationToken);
            await foreach (var regra in regrasDoBroker)
            {
                var filtro = DescricaoDeRegras.DescreverFiltro(regra);
                var descricaoDaAcao = DescricaoDeRegras.DescreverAcao(regra);
                var acao = descricaoDaAcao is null ? "" : $" | action: {descricaoDaAcao}";
                var linha = $"regra {regra.Name}: {filtro}{acao}";
                logger.LogInformation("  {Regra}", linha);
                regras.Add(linha);
            }
            resultado.Add(new RegrasDaAssinatura(assinatura.Topico, assinatura.Nome, [.. regras]));
        }
        return [.. resultado];
    }

    private async Task<MensagemEspiada[]> EspiarAsync(
        ServiceBusReceiver receiver, string nome, bool deadLetter, CancellationToken cancellationToken)
    {
        var mensagens = await receiver.PeekMessagesAsync(MaximoDeMensagens, cancellationToken: cancellationToken);
        logger.LogInformation("{Quantidade} mensagem(ns) em '{Entidade}'.", mensagens.Count, nome);

        var espiadas = new List<MensagemEspiada>();
        foreach (var m in mensagens)
        {
            // Mostra as propriedades: é o que os filtros enxergam (a action da regra pode ter adicionado alguma).
            var pares = m.ApplicationProperties.Select(p => $"{p.Key}={p.Value}");
            var propriedades = string.Join(", ", pares);
            var corpo = ResumirCorpo(m);
            var motivo = deadLetter ? $" motivo: {m.DeadLetterReason} | {m.DeadLetterErrorDescription}" : "";
            logger.LogInformation(
                "  #{Seq} {Subject} {MessageId} [{Propriedades}] entregas={Entregas} corpo={Corpo}{Motivo}",
                m.SequenceNumber, m.Subject, m.MessageId, propriedades, m.DeliveryCount, corpo, motivo);
            var espiada = new MensagemEspiada(
                m.SequenceNumber, m.MessageId, m.Subject, m.DeliveryCount, m.ApplicationProperties,
                m.DeadLetterReason, m.DeadLetterErrorDescription);
            espiadas.Add(espiada);
        }
        return [.. espiadas];
    }

    /// <summary>
    /// Começo do corpo como texto. Peek serve justamente para investigar mensagem venenosa: sem desserializar,
    /// não quebra com corpo ilegível e não precisa conhecer o contrato.
    /// </summary>
    private static string ResumirCorpo(ServiceBusReceivedMessage m)
    {
        var texto = m.Body.ToString();
        return texto.Length > TamanhoDoResumoDoCorpo ? texto[..TamanhoDoResumoDoCorpo] + "..." : texto;
    }
}
