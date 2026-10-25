namespace Vida.Producer.Contratos;

/// <summary>
/// Corpo de POST /outbox/simulacao.
/// </summary>
/// <param name="Indisponivel">True derruba o envio do despachante; false restabelece.</param>
public sealed record SimulacaoDoOutbox(bool Indisponivel);
