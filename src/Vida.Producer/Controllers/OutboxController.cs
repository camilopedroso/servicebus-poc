using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;

namespace Vida.Producer.Controllers;

/// <summary>
/// Operação do outbox (fase 4): quantas mensagens estão pendentes e publicadas. A simulação de indisponibilidade fica em Demonstracao/SimulacaoDoOutboxController.
/// </summary>
/// <param name="simulacao">Para informar se a simulação de indisponibilidade está ligada.</param>
/// <param name="logger">Logger; aparece com o selo "outbox" na coluna do Producer.</param>
[ApiController]
[Route("outbox")]
[Tags("Operação")]
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
}
