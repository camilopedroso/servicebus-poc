using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vida.Plataforma.Api;
using Vida.Subscricao.Contratos;
using Vida.Subscricao.Dados;
using Vida.Subscricao.Endossos;

namespace Vida.Subscricao.Controllers;

/// <summary>
/// API REST das apólices da Subscrição: consulta e pedido de endosso. O POST grava o endosso e o comando no outbox
/// numa transação e responde 202 sem falar com o Service Bus: o endosso é aplicado depois, pelo ProcessadorDeEndossos.
/// </summary>
/// <param name="banco">DbContext da requisição (scoped).</param>
/// <param name="registro">Grava o pedido de endosso (versão + outbox).</param>
/// <param name="logger">Logger da API de apólices.</param>
[ApiController]
[Route("api/apolices")]
[Tags("Apólices")]
public sealed class ApolicesController(
    BancoDaSubscricao banco,
    RegistroDeEndossos registro,
    ILogger<ApolicesController> logger) : ControllerBase
{
    /// <summary>
    /// Cabeçalho que torna o POST seguro para repetir (mesma chave na mesma apólice = mesmo endosso).
    /// </summary>
    public const string CabecalhoDeIdempotencia = "Idempotency-Key";

    /// <summary>
    /// GET /api/apolices/{apoliceId}: 200 com a apólice e os endossos, ou 404.
    /// </summary>
    [HttpGet("{apoliceId:guid}")]
    [EndpointName("ObterApolice")]
    [ProducesResponseType<ApoliceResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApoliceResposta>> ObterAsync(Guid apoliceId, CancellationToken cancellationToken)
    {
        var apolices = banco.Apolices.AsNoTracking();
        var apolice = await apolices.FirstOrDefaultAsync(a => a.ApoliceId == apoliceId, cancellationToken);
        if (apolice is null)
        {
            return ApoliceNaoEncontrada(apoliceId);
        }

        var endossosSomenteLeitura = banco.Endossos.AsNoTracking();
        var daApolice = endossosSomenteLeitura.Where(e => e.ApoliceId == apoliceId);
        var emOrdem = daApolice.OrderBy(e => e.Versao);
        var endossos = await emOrdem.ToListAsync(cancellationToken);
        return ApoliceResposta.De(apolice, endossos);
    }

    /// <summary>
    /// POST /api/apolices/{apoliceId}/endossos: 202 Accepted (aplicação assíncrona); 200 se a Idempotency-Key já foi
    /// usada nesta apólice; 400 se inválido; 404 se a apólice não existe.
    /// </summary>
    [HttpPost("{apoliceId:guid}/endossos")]
    [EndpointName("SolicitarEndosso")]
    [ProducesResponseType<EndossoResposta>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EndossoResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EndossoResposta>> SolicitarEndossoAsync(
        Guid apoliceId,
        NovoEndosso corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chaveRecebida,
        CancellationToken cancellationToken)
    {
        // Cabeçalho em branco vale como ausente; espaços nas pontas não fazem parte da chave.
        var chave = string.IsNullOrWhiteSpace(chaveRecebida) ? null : chaveRecebida.Trim();

        var erros = corpo.Validar();
        if (chave is { Length: > EndossoRegistrado.TamanhoMaximoDaChave })
        {
            erros[CabecalhoDeIdempotencia] = [$"Use no máximo {EndossoRegistrado.TamanhoMaximoDaChave} caracteres."];
        }
        if (erros.Count > 0)
        {
            return this.ProblemaDeValidacao(erros);
        }

        var tipo = corpo.Tipo!.Trim();
        var pedido = await registro.SolicitarAsync(apoliceId, tipo, chave, cancellationToken);
        if (pedido.Situacao == SituacaoDoPedidoDeEndosso.ApoliceInexistente)
        {
            return ApoliceNaoEncontrada(apoliceId);
        }

        var endosso = pedido.Endosso!;
        var resposta = EndossoResposta.De(endosso);
        if (pedido.Situacao == SituacaoDoPedidoDeEndosso.Repetido)
        {
            // 200 (não 202): nada foi aceito agora. O cabeçalho deixa explícito que é uma resposta repetida.
            Response.Headers["Idempotent-Replayed"] = "true";
            logger.LogInformation(
                "API: Idempotency-Key repetida na apólice {ApoliceId}. Devolvendo o endosso v{Versao} já pedido.",
                apoliceId, endosso.Versao);
            return Ok(resposta);
        }

        logger.LogInformation(
            "API: endosso v{Versao} ({Tipo}) da apólice {ApoliceId} gravado com o comando no outbox.",
            endosso.Versao, endosso.Tipo, apoliceId);
        // Location aponta para a apólice: é lá que o cliente acompanha quando o endosso foi aplicado.
        return Accepted($"/api/apolices/{apoliceId}", resposta);
    }

    private ObjectResult ApoliceNaoEncontrada(Guid apoliceId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Apólice não encontrada",
            detail: $"Nenhuma apólice com id {apoliceId}.");
}
