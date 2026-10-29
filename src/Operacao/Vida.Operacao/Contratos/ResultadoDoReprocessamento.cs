namespace Vida.Operacao.Contratos;

/// <summary>
/// Resposta de POST /filas/{nome}/dlq/reprocessar.
/// </summary>
/// <param name="Movidas">Mensagens devolvidas à fila principal.</param>
/// <param name="Mantidas">Mensagens que ficaram na DLQ (motivo permanente ou chegaram durante o reprocessamento).</param>
public sealed record ResultadoDoReprocessamento(int Movidas, int Mantidas);
