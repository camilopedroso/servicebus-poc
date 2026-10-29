using Azure.Messaging.ServiceBus;
using Vida.Subscricao.Contratos.Eventos;

namespace Vida.Subscricao.Contratos.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo PropostaRecusada.
/// </summary>
public static class MensagensRecusa
{
    /// <summary>Subject da mensagem ("PropostaRecusada"): o tipo do evento, que os filtros de assinatura podem usar.</summary>
    public const string Tipo = "PropostaRecusada";

    /// <summary>Tópico onde a Subscrição publica a recusa por regra de negócio (um desfecho, não uma falha).</summary>
    public const string Topico = "proposta-recusada";

    /// <summary>
    /// Fábrica que monta a mensagem do evento de recusa.
    /// </summary>
    /// <param name="recusa">Evento PropostaRecusada a ser enviado.</param>
    /// <returns>Mensagem do Service Bus pronta para envio.</returns>
    public static ServiceBusMessage Criar(PropostaRecusada recusa)
    {
        var corpo = BinaryData.FromObjectAsJson(recusa);
        var mensagem = new ServiceBusMessage(corpo)
        {
            // Uma recusa por proposta: o id da proposta serve de MessageId (base para detecção de duplicatas, fase 3).
            MessageId = recusa.PropostaId.ToString(),
            ContentType = "application/json",
            Subject = Tipo,
            CorrelationId = recusa.PropostaId.ToString(),
        };
        mensagem.ApplicationProperties[PropriedadesDeRoteamento.Plano] = recusa.Plano.ToString();
        return mensagem;
    }
}
