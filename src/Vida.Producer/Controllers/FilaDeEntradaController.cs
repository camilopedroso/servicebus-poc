using Microsoft.AspNetCore.Mvc;
using Vida.Producer.Operacao;
using Vida.Producer.Contratos;

namespace Vida.Producer.Controllers;

/// <summary>
/// Ferramentas de operação da fila "subscricao-entrada", a caixa de entrada da Subscrição: peek e reprocessamento da DLQ.
/// </summary>
/// <param name="inspecao">Peek na fila e na DLQ.</param>
/// <param name="reprocessamento">Devolve a DLQ para a fila.</param>
[ApiController]
[Route("filas/subscricao-entrada")]
[Tags("Operação")]
public sealed class FilaDeEntradaController(InspecaoDeEntidades inspecao, ReprocessamentoDaDlq reprocessamento) : ControllerBase
{
    /// <summary>
    /// GET /filas/subscricao-entrada/mensagens?dlq=: peek na fila (ou na DLQ, com o motivo).
    /// </summary>
    [HttpGet("mensagens")]
    public Task<MensagemEspiada[]> EspiarAsync([FromQuery] bool? dlq, CancellationToken cancellationToken) =>
        inspecao.EspiarFilaAsync(dlq ?? false, cancellationToken);

    /// <summary>
    /// POST /filas/subscricao-entrada/dlq/reprocessar: devolve a DLQ para a fila (payload inválido fica na DLQ).
    /// </summary>
    [HttpPost("dlq/reprocessar")]
    public Task<ResultadoDoReprocessamento> ReprocessarDlqAsync(CancellationToken cancellationToken) =>
        reprocessamento.ReprocessarAsync(cancellationToken);
}
