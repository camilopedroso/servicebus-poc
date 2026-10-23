using Azure.Messaging.ServiceBus;

namespace Vida.Mensageria;

public static class MensagensApolice
{
    /// <summary>Monta a mensagem do evento, com as propriedades que os filtros das assinaturas usam.</summary>
    public static ServiceBusMessage Criar(ApoliceEmitida apolice)
    {
        var mensagem = new ServiceBusMessage(BinaryData.FromObjectAsJson(apolice))
        {
            MessageId = apolice.ApoliceId.ToString(),
            ContentType = "application/json",
            Subject = TiposDeMensagem.ApoliceEmitida,
            CorrelationId = apolice.PropostaId.ToString(),
        };
        mensagem.ApplicationProperties[Propriedades.Plano] = apolice.Plano;
        mensagem.ApplicationProperties[Propriedades.CapitalSegurado] = (long)apolice.CapitalSegurado;
        return mensagem;
    }

    /// <summary>Regra de prêmio fictícia, só para o evento ter um valor calculado.</summary>
    public static decimal CalcularPremioMensal(decimal capitalSegurado, int idade) =>
        Math.Round(capitalSegurado * (0.0002m + idade * 0.00001m), 2);
}
