namespace Vida.Propostas.Contratos.Mensagens;

/// <summary>
/// Application properties da mensagem de proposta (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// Até a 1.1.0 havia também as de simulação (falha, backoff, tentativa, queda); saíram na 2.0.0 (fase 8).
/// </summary>
public static class PropriedadesDaProposta
{
    public const string Plano = "plano";
}
