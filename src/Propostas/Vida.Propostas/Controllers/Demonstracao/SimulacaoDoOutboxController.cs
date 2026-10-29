using Microsoft.AspNetCore.Mvc;
using Vida.Propostas.Contratos.Demonstracao;
using Vida.Propostas.Outbox;

namespace Vida.Propostas.Controllers.Demonstracao;

/// <summary>
/// Demonstração do outbox (fase 4): simula o Service Bus fora do ar só para o despachante.
/// </summary>
/// <param name="simulacao">Chave liga/desliga que o despachante consulta antes de enviar.</param>
/// <param name="logger">Logger; aparece com o selo "outbox" na coluna de Propostas.</param>
[ApiController]
[Route("demo/outbox")]
[Tags("Demonstração")]
[ApiExplorerSettings(GroupName = "demo")]
public sealed class SimulacaoDoOutboxController(
    SimulacaoDeIndisponibilidade simulacao,
    ILogger<SimulacaoDoOutboxController> logger) : ControllerBase
{
    /// <summary>
    /// POST /demo/outbox/simulacao: derruba ou restabelece o envio do despachante.
    /// </summary>
    [HttpPost("simulacao")]
    public IActionResult Simular(SimulacaoDoOutbox corpo)
    {
        simulacao.Ativa = corpo.Indisponivel;
        logger.LogWarning(corpo.Indisponivel
            ? "Simulação: Service Bus FORA DO AR para o despachante do outbox. A API continua aceitando propostas."
            : "Simulação: Service Bus de volta. O despachante publica o que acumulou.");
        return Ok(new { corpo.Indisponivel });
    }
}
