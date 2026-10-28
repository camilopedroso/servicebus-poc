using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;

namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo PropostaRecusada.
/// </summary>
public static class MensagensRecusa
{
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
            Subject = TiposDeMensagem.PropostaRecusada,
            CorrelationId = recusa.PropostaId.ToString(),
        };
        mensagem.ApplicationProperties[Propriedades.Plano] = recusa.Plano.ToString();
        return mensagem;
    }
}
