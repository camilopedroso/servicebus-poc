using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Vida.Producer.Outbox;

/// <summary>
/// Uma linha da tabela OutboxMensagens: a mensagem do Service Bus serializada, gravada na MESMA transação
/// do dado de negócio. O despachante lê, publica e marca como publicada.
/// </summary>
public sealed class MensagemDeSaida
{
    /// <summary>Identity: dá a ordem de publicação.</summary>
    public long Id { get; set; }

    /// <summary>Fila ou tópico de destino.</summary>
    public required string Destino { get; set; }

    public required string MessageId { get; set; }

    public string? Subject { get; set; }

    public string? CorrelationId { get; set; }

    public string? ContentType { get; set; }

    public required string Corpo { get; set; }

    /// <summary>Application properties em JSON (os filtros de assinatura dependem delas).</summary>
    public required string Propriedades { get; set; }

    public DateTimeOffset CriadaEm { get; set; }

    /// <summary>Null = pendente.</summary>
    public DateTimeOffset? PublicadaEm { get; set; }

    public int Tentativas { get; set; }

    /// <summary>Depois de uma falha, o despachante só tenta de novo a partir daqui (backoff).</summary>
    public DateTimeOffset? ProximaTentativaEm { get; set; }

    public string? UltimoErro { get; set; }

    /// <summary>
    /// Guarda uma ServiceBusMessage já montada (pela mesma fábrica do envio direto) como linha do outbox.
    /// </summary>
    /// <param name="destino">Fila ou tópico.</param>
    /// <param name="mensagem">Mensagem a publicar depois.</param>
    /// <returns>Linha pronta para o DbContext.</returns>
    public static MensagemDeSaida De(string destino, ServiceBusMessage mensagem) => new()
    {
        Destino = destino,
        MessageId = mensagem.MessageId,
        Subject = mensagem.Subject,
        CorrelationId = mensagem.CorrelationId,
        ContentType = mensagem.ContentType,
        Corpo = mensagem.Body.ToString(),
        Propriedades = JsonSerializer.Serialize(mensagem.ApplicationProperties),
        CriadaEm = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Remonta a ServiceBusMessage. O MessageId é o mesmo da gravação: se o despachante publicar e cair antes
    /// de marcar a linha, a nova publicação sai com o mesmo id (duplicata detectável).
    /// </summary>
    /// <returns>Mensagem pronta para envio.</returns>
    public ServiceBusMessage ParaServiceBus()
    {
        var mensagem = new ServiceBusMessage(BinaryData.FromString(Corpo))
        {
            MessageId = MessageId,
            Subject = Subject,
            CorrelationId = CorrelationId,
            ContentType = ContentType,
        };
        var propriedades = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Propriedades) ?? [];
        foreach (var (nome, valor) in propriedades)
        {
            // JSON não guarda o tipo .NET: volta para bool/long/double/string, que é o que os filtros comparam.
            mensagem.ApplicationProperties[nome] = valor.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when valor.TryGetInt64(out var inteiro) => inteiro,
                JsonValueKind.Number => valor.GetDouble(),
                _ => valor.ToString(),
            };
        }
        return mensagem;
    }
}
