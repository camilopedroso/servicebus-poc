using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Vida.Subscricao.Outbox;

/// <summary>
/// Uma linha da tabela Subscricao.OutboxMensagens: a mensagem do Service Bus serializada, gravada na MESMA
/// transação do dado de negócio (apólice, recusa ou endosso) e, quando há mensagem recebida, da inbox. O despachante lê, publica e marca como publicada.
/// Cópia do padrão de Propostas, não código compartilhado: o outbox é um padrão, e cada squad tem o seu.
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

    /// <summary>
    /// Sessão da mensagem, para destinos com sessions (a fila endossos exige: sem SessionId, o envio é recusado).
    /// </summary>
    public string? SessionId { get; set; }

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
    public static MensagemDeSaida De(string destino, ServiceBusMessage mensagem)
    {
        // Contexto de trace (W3C traceparent) de AGORA, o processamento da proposta que gerou a mensagem. O SDK do
        // Service Bus só preenche o Diagnostic-Id no envio, e o envio acontece depois, no despachante: sem isto,
        // o trace de Cobrança e Notificação ficaria pendurado no despachante, e não na análise da proposta.
        if (Activity.Current is { } atual && !mensagem.ApplicationProperties.ContainsKey(PropriedadeDeTrace))
        {
            mensagem.ApplicationProperties[PropriedadeDeTrace] = atual.Id;
        }
        var corpo = mensagem.Body.ToString();
        var propriedades = JsonSerializer.Serialize(mensagem.ApplicationProperties);
        return new()
        {
            Destino = destino,
            MessageId = mensagem.MessageId,
            Subject = mensagem.Subject,
            CorrelationId = mensagem.CorrelationId,
            SessionId = mensagem.SessionId,
            ContentType = mensagem.ContentType,
            Corpo = corpo,
            Propriedades = propriedades,
            CriadaEm = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Propriedade em que o SDK do Service Bus carrega o contexto de trace entre producer e consumer.</summary>
    public const string PropriedadeDeTrace = "Diagnostic-Id";

    /// <summary>
    /// Remonta a ServiceBusMessage. O MessageId é o mesmo da gravação: se o despachante publicar e cair antes
    /// de marcar a linha, a nova publicação sai com o mesmo id (duplicata detectável).
    /// </summary>
    /// <returns>Mensagem pronta para envio.</returns>
    public ServiceBusMessage ParaServiceBus()
    {
        var bytes = BinaryData.FromString(Corpo);
        var mensagem = new ServiceBusMessage(bytes)
        {
            MessageId = MessageId,
            Subject = Subject,
            CorrelationId = CorrelationId,
            SessionId = SessionId,
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
