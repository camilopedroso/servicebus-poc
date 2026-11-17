namespace Vida.Subscricao.Analise;

/// <summary>
/// Application properties que só a própria Subscrição escreve e lê, nas cópias que ela agenda na fila
/// subscricao-entrada. Por isso ficam aqui, e não no contrato de Propostas (até a 1.1.0, ficavam lá).
/// </summary>
public static class PropriedadesDaRetentativa
{
    /// <summary>
    /// Número da tentativa no retry com backoff. Cada cópia agendada é uma mensagem nova (DeliveryCount volta a 1),
    /// então a contagem precisa viajar na própria mensagem. Ausente = primeira tentativa.
    /// </summary>
    public const string Tentativa = "tentativa";
}
