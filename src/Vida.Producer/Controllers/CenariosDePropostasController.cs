using Microsoft.AspNetCore.Mvc;
using Vida.Mensageria.Contratos;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;

namespace Vida.Producer.Controllers;

/// <summary>
/// Cenários que enviam propostas direto para a fila "propostas" (fases 1 e 3) e o dual write sem outbox (fase 4).
/// Não é a API de negócio (essa é a PropostasController): são os botões do painel.
/// </summary>
/// <param name="envio">Envio direto para a fila.</param>
[ApiController]
[Route("propostas")]
[Tags("Cenários")]
public sealed class CenariosDePropostasController(EnvioDePropostas envio) : ControllerBase
{
    /// <summary>
    /// POST /propostas/lote: propostas válidas, enviadas em lote.
    /// </summary>
    [HttpPost("lote")]
    public async Task<IActionResult> EnviarLoteAsync(EnvioDeLote corpo, CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return this.ProblemaDeValidacao(erros);
        }
        var propostas = await envio.EnviarLoteAsync(corpo.Quantidade, cancellationToken);
        return Ok(new { Propostas = propostas });
    }

    /// <summary>
    /// POST /propostas: uma proposta. Idade > 80 → recusa; simularFalha → retry até maxDeliveryCount → DLQ.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PropostaSeguroVida>> EnviarUmaAsync(EnvioDeProposta corpo, CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return this.ProblemaDeValidacao(erros);
        }
        return await envio.EnviarUmaAsync(corpo, cancellationToken);
    }

    /// <summary>
    /// POST /propostas/invalida: corpo ilegível → DLQ na primeira entrega (PayloadInvalido).
    /// </summary>
    [HttpPost("invalida")]
    public async Task<IActionResult> EnviarInvalidaAsync(CancellationToken cancellationToken)
    {
        var messageId = await envio.EnviarInvalidaAsync(cancellationToken);
        return Ok(new { MessageId = messageId });
    }

    /// <summary>
    /// POST /propostas/sem-outbox: grava no banco e publica, sem outbox. O problema que o outbox resolve.
    /// </summary>
    /// <remarks>
    /// O banco vem por [FromServices], e não pelo construtor: sem a connection string, resolver o DbContext lança
    /// BancoNaoConfiguradoException, e os outros cenários deste controller (que não usam banco) parariam de funcionar.
    /// </remarks>
    [HttpPost("sem-outbox")]
    public async Task<IActionResult> EnviarSemOutboxAsync(
        EnvioSemOutboxParametros corpo,
        [FromServices] EnvioSemOutbox envioSemOutbox,
        [FromServices] BancoDePropostas banco,
        CancellationToken cancellationToken)
    {
        var (proposta, publicada) = await envioSemOutbox.EnviarAsync(banco, corpo.FalharAntesDoEnvio, cancellationToken);
        return Ok(new { proposta.PropostaId, Publicada = publicada });
    }
}
