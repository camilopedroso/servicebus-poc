namespace Vida.Propostas.Contratos.Demonstracao;

/// <summary>
/// Corpo de POST /demo/outbox/simulacao.
/// </summary>
/// <param name="Indisponivel">True derruba o envio do despachante; false restabelece.</param>
public sealed record SimulacaoDoOutbox(bool Indisponivel);
