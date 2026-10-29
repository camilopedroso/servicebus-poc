namespace Vida.Propostas.Contratos.Mensagens;

/// <summary>
/// Application properties da mensagem de proposta (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// As de simulação (falha, backoff, tentativa) só existem para os cenários de estudo.
/// </summary>
public static class PropriedadesDaProposta
{
    public const string Plano = "plano";

    /// <summary>Quando true, o consumer simula uma falha transitória (para exercitar retry → DLQ).</summary>
    public const string SimularFalha = "simularFalha";

    /// <summary>
    /// Quando true junto com SimularFalha, o consumer não abandona: agenda uma cópia com espera crescente (fase 3).
    /// </summary>
    public const string RetryComBackoff = "retryComBackoff";

    /// <summary>
    /// Número da tentativa no retry com backoff. Cada cópia agendada é uma mensagem nova (DeliveryCount volta a 1),
    /// então a contagem precisa viajar na própria mensagem.
    /// </summary>
    public const string Tentativa = "tentativa";
}
