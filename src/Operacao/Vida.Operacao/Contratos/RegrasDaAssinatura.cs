namespace Vida.Operacao.Contratos;

/// <summary>
/// Regras que o broker aplicou a uma assinatura.
/// </summary>
/// <param name="Topico">Tópico da assinatura.</param>
/// <param name="Assinatura">Nome da assinatura.</param>
/// <param name="Regras">Uma linha por regra: nome, filtro e action.</param>
public sealed record RegrasDaAssinatura(string Topico, string Assinatura, string[] Regras);
