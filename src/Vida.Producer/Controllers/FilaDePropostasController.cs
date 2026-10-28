using Microsoft.AspNetCore.Mvc;
using Vida.Producer.Operacao;
using Vida.Producer.Contratos;

namespace Vida.Producer.Controllers;

/// <summary>
/// Ferramentas de operação da fila "propostas" (fase 1): peek e reprocessamento da DLQ.
/// </summary>
/// <param name="inspecao">Peek na fila e na DLQ.</param>
/// <param name="reprocessamento">Devolve a DLQ para a fila.</param>
[ApiController]
[Route("filas/propostas")]
[Tags("Operação")]
public sealed class FilaDePropostasController(InspecaoDeEntidades inspecao, ReprocessamentoDaDlq reprocessamento) : ControllerBase
{
    /// <summary>
    /// GET /filas/propostas/mensagens?dlq=: peek na fila (ou na DLQ, com o motivo).
    /// </summary>
    [HttpGet("mensagens")]
    public Task<MensagemEspiada[]> EspiarAsync([FromQuery] bool? dlq, CancellationToken cancellationToken) =>
        inspecao.EspiarFilaAsync(dlq ?? false, cancellationToken);

    /// <summary>
    /// POST /filas/propostas/dlq/reprocessar: devolve a DLQ para a fila (payload inválido fica na DLQ).
    /// </summary>
    [HttpPost("dlq/reprocessar")]
    public Task<ResultadoDoReprocessamento> ReprocessarDlqAsync(CancellationToken cancellationToken) =>
        reprocessamento.ReprocessarAsync(cancellationToken);
}
