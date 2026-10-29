using Azure.Messaging.ServiceBus;
using Vida.Subscricao.Contratos.Eventos;

namespace Vida.Subscricao.Contratos.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo ApoliceEmitida.
/// </summary>
public static class MensagensApolice
{
    /// <summary>Subject da mensagem ("ApoliceEmitida"): o tipo do evento, que os filtros de assinatura podem usar.</summary>
    public const string Tipo = "ApoliceEmitida";

    /// <summary>
    /// Tópico onde a Subscrição publica a apólice emitida. Cada interessado (Cobrança, Notificação, Resseguro,
    /// Propostas) cria a sua assinatura nele, no Bicep da própria squad.
    /// </summary>
    public const string Topico = "apolice-emitida";

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
            Subject = Tipo,
            CorrelationId = apolice.PropostaId.ToString(),
        };
        mensagem.ApplicationProperties[PropriedadesDeRoteamento.Plano] = apolice.Plano.ToString();
        mensagem.ApplicationProperties[PropriedadesDeRoteamento.CapitalSegurado] = (long)apolice.CapitalSegurado;
        mensagem.ApplicationProperties[PropriedadesDeRoteamento.RequerRevisao] = apolice.RequerRevisao;
        return mensagem;
    }
}
