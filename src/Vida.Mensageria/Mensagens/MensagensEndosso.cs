using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;

namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo EndossoSolicitado.
/// </summary>
public static class MensagensEndosso
{
    /// <summary>
    /// Monta a mensagem do endosso para a fila "endossos".
    /// </summary>
    /// <param name="endosso">Endosso a enviar.</param>
    /// <returns>Mensagem do Service Bus pronta para envio.</returns>
    public static ServiceBusMessage Criar(EndossoSolicitado endosso) => new(BinaryData.FromObjectAsJson(endosso))
    {
        // Sessão = apólice: as versões de uma apólice saem em ordem; apólices diferentes rodam em paralelo.
        // ≈ chave de partição no Kafka, mas sem número fixo de partições.
        SessionId = endosso.ApoliceId.ToString(),
        // Id composto, previsível a partir do negócio: reenviar a mesma versão gera o mesmo MessageId,
        // e a detecção de duplicatas da fila descarta a cópia. Um Guid novo a cada envio anularia isso.
        MessageId = $"{endosso.ApoliceId}/v{endosso.Versao}",
        ContentType = "application/json",
        Subject = TiposDeMensagem.EndossoSolicitado,
        ApplicationProperties = { [Propriedades.Versao] = endosso.Versao },
    };
}
