using Azure.Messaging.ServiceBus;
using Vida.Propostas.Contratos.Eventos;

namespace Vida.Propostas.Contratos.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo PropostaSeguroVida.
/// Usado pelo outbox da API de propostas: a mensagem é montada aqui e guardada como linha do outbox.
/// </summary>
public static class MensagensProposta
{
    /// <summary>Subject da mensagem ("PropostaRecebida"): o tipo do evento, que os filtros de assinatura podem usar.</summary>
    public const string Tipo = "PropostaRecebida";

    /// <summary>
    /// Tópico onde a squad de Propostas publica o fato "proposta recebida". Ela não sabe quem reage: a Subscrição
    /// recebe pela assinatura "subscricao" (que encaminha para a fila dela), e um interessado novo só cria a sua.
    /// </summary>
    public const string Topico = "proposta-recebida";

    /// <summary>
    /// Monta a mensagem da proposta para o tópico "proposta-recebida".
    /// </summary>
    /// <param name="proposta">Proposta de seguro de vida.</param>
    /// <returns>Mensagem do Service Bus pronta para envio.</returns>
    public static ServiceBusMessage Criar(PropostaSeguroVida proposta)
    {
        var corpo = BinaryData.FromObjectAsJson(proposta);
        var mensagem = new ServiceBusMessage(corpo)
        {
            // MessageId = id de negócio: base para detecção de duplicatas.
            MessageId = proposta.PropostaId.ToString(),
            ContentType = "application/json",
            // Subject (antigo "Label") descreve o tipo da mensagem; filtros de assinatura podem usar.
            Subject = Tipo,
            CorrelationId = proposta.ClienteId,
        };
        mensagem.ApplicationProperties[PropriedadesDaProposta.Plano] = proposta.Plano.ToString();
        return mensagem;
    }
}
