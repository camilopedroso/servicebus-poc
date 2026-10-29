using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vida.Propostas.Contratos;
using Vida.Propostas.Dados;
using Vida.Propostas.Outbox;

namespace Vida.Propostas.Controllers.Demonstracao;

/// <summary>
/// Estado do outbox (fase 4): quantas mensagens estão pendentes e publicadas. Só os cenários do painel usam, então é
/// demonstração. O outbox mora no banco de Propostas: a ferramenta de operação (Vida.Operacao) não o enxerga, e nem
/// deve (cada serviço lê só o próprio banco). A simulação de indisponibilidade fica em SimulacaoDoOutboxController.
/// </summary>
/// <param name="simulacao">Para informar se a simulação de indisponibilidade está ligada.</param>
/// <param name="logger">Logger; aparece com o selo "outbox" na coluna de Propostas.</param>
[ApiController]
[Route("demo/outbox")]
[Tags("Demonstração")]
[ApiExplorerSettings(GroupName = "demo")]
public sealed class OutboxController(SimulacaoDeIndisponibilidade simulacao, ILogger<OutboxController> logger) : ControllerBase
{
    /// <summary>
    /// GET /demo/outbox: quantas mensagens estão pendentes e publicadas, e se a simulação está ligada.
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
