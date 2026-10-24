namespace Vida.Producer.Contratos;

/// <summary>
/// Regras que o broker aplicou a uma assinatura.
/// </summary>
/// <param name="Assinatura">Nome da assinatura.</param>
/// <param name="Regras">Uma linha por regra: nome, filtro e action.</param>
public sealed record RegrasDaAssinatura(string Assinatura, string[] Regras);
