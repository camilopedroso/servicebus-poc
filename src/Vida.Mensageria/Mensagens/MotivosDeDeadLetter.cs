namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Valores de DeadLetterReason usados pelos consumers. O broker usa os seus próprios
/// (ex.: MaxDeliveryCountExceeded, TTLExpiredException), que não estão aqui.
/// Recusa por regra de negócio NÃO é dead-letter: vira o evento PropostaRecusada.
/// </summary>
public static class MotivosDeDeadLetter
{
    /// <summary>O corpo não pôde ser lido. Reenviar a mesma mensagem dá o mesmo erro.</summary>
    public const string PayloadInvalido = "PayloadInvalido";

    /// <summary>
    /// Motivos em que reprocessar não adianta: a mensagem voltaria para a DLQ na hora.
    /// Precisam de correção (dado ou código) antes de reenviar.
    /// </summary>
    public static readonly string[] Permanentes = [PayloadInvalido];
}
