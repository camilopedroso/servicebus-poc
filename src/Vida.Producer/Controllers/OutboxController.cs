using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;

namespace Vida.Producer.Controllers;

/// <summary>
/// Operação do outbox (fase 4): ver pendentes e simular o Service Bus fora do ar.
/// </summary>
/// <param name="simulacao">Chave liga/desliga que o despachante consulta antes de enviar.</param>
/// <param name="logger">Logger; aparece com o selo "outbox" na coluna do Producer.</param>
[ApiController]
[Route("outbox")]
public sealed class OutboxController(SimulacaoDeIndisponibilidade simulacao, ILogger<OutboxController> logger) : ControllerBase
{
    /// <summary>
    /// GET /outbox: quantas mensagens estão pendentes e publicadas, e se a simulação está ligada.
    /// </summary>
    [HttpGet]
    public async Task<EstadoDoOutbox> ObterEstadoAsync([FromServices] BancoDePropostas banco, CancellationToken cancellationToken)
    {
        var pendentes = await banco.OutboxMensagens.CountAsync(m => m.PublicadaEm == null, cancellationToken);
        var publicadas = await banco.OutboxMensagens.CountAsync(m => m.PublicadaEm != null, cancellationToken);
        var situacao = simulacao.Ativa ? "indisponível (simulação)" : "disponível";
        logger.LogInformation(
            "Outbox: {Pendentes} pendente(s), {Publicadas} publicada(s). Service Bus {Situacao}.",
            pendentes, publicadas, situacao);
        return new EstadoDoOutbox(simulacao.Ativa, pendentes, publicadas);
    }

    /// <summary>
    /// POST /outbox/simulacao: derruba ou restabelece o envio do despachante.
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
