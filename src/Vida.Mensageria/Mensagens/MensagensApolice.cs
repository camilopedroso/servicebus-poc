using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;

namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo ApoliceEmitida.
/// </summary>
public static class MensagensApolice
{
    /// <summary>
    /// Fábrica que monta a mensagem do evento, com as propriedades que os filtros das assinaturas usam.
    /// </summary>
    /// <param name="apolice">Evento ApoliceEmitida a ser enviado.</param>
    /// <returns>Mensagem do Service Bus pronta para envio.</returns>
    public static ServiceBusMessage Criar(ApoliceEmitida apolice)
    {
        var corpo = BinaryData.FromObjectAsJson(apolice);
        var mensagem = new ServiceBusMessage(corpo)
        {
            MessageId = apolice.ApoliceId.ToString(),
            ContentType = "application/json",
            Subject = TiposDeMensagem.ApoliceEmitida,
            CorrelationId = apolice.PropostaId.ToString(),
        };
        mensagem.ApplicationProperties[Propriedades.Plano] = apolice.Plano;
        mensagem.ApplicationProperties[Propriedades.CapitalSegurado] = (long)apolice.CapitalSegurado;
        mensagem.ApplicationProperties[Propriedades.RequerRevisao] = apolice.RequerRevisao;
        return mensagem;
    }
}
