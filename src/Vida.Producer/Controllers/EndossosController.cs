using Microsoft.AspNetCore.Mvc;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos;

namespace Vida.Producer.Controllers;

/// <summary>
/// Fase 3: cenários da fila "endossos" (sessions e detecção de duplicatas).
/// </summary>
/// <param name="envio">Envio de endossos.</param>
[ApiController]
[Route("endossos")]
[Tags("Cenários")]
public sealed class EndossosController(EnvioDeEndossos envio) : ControllerBase
{
    /// <summary>
    /// POST /endossos/em-ordem: v1..vN intercaladas entre apólices (sessões em paralelo).
    /// </summary>
    [HttpPost("em-ordem")]
    public async Task<IActionResult> EnviarEmOrdemAsync(SequenciaDeEndossos corpo, CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return this.ProblemaDeValidacao(erros);
        }
        return Ok(new { Apolices = await envio.EnviarEmOrdemAsync(corpo.Apolices, corpo.Versoes, cancellationToken) });
    }

    /// <summary>
    /// POST /endossos/fora-de-ordem: 5 versões de uma apólice embaralhadas → deferral.
    /// </summary>
    [HttpPost("fora-de-ordem")]
    public async Task<IActionResult> EnviarForaDeOrdemAsync(CancellationToken cancellationToken)
    {
        var (apoliceId, ordem) = await envio.EnviarForaDeOrdemAsync(versoes: 5, cancellationToken);
        return Ok(new { ApoliceId = apoliceId, Ordem = ordem });
    }

    /// <summary>
    /// POST /endossos/duplicado: v1, v1 de novo (mesmo MessageId) e v2 → o broker descarta a cópia.
    /// </summary>
    [HttpPost("duplicado")]
    public async Task<IActionResult> EnviarDuplicadoAsync(CancellationToken cancellationToken) =>
        Ok(new { ApoliceId = await envio.EnviarDuplicadoAsync(cancellationToken) });
}
