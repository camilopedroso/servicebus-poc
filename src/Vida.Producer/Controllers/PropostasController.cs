using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;

namespace Vida.Producer.Controllers;

/// <summary>
/// Fase 4: API REST de propostas. O POST grava a proposta e a mensagem no outbox numa única transação
/// do SQL Server e responde 201 sem falar com o Service Bus: quem publica é o DespachanteDoOutbox.
/// </summary>
/// <param name="banco">DbContext da requisição (scoped).</param>
/// <param name="logger">Logger; aparece com o selo "API" na coluna do Producer.</param>
[ApiController]
[Route("api/propostas")]
[Tags("Propostas")]
public sealed class PropostasController(BancoDePropostas banco, ILogger<PropostasController> logger) : ControllerBase
{
    /// <summary>Cabeçalho que torna o POST seguro para repetir (mesma chave = mesma proposta).</summary>
    public const string CabecalhoDeIdempotencia = "Idempotency-Key";

    /// <summary>
    /// POST /api/propostas: 201 Created com Location; 200 se a Idempotency-Key já foi usada; 400 se inválida.
    /// </summary>
    [HttpPost]
    [EndpointName("CriarProposta")]
    [ProducesResponseType<PropostaResposta>(StatusCodes.Status201Created)]
    [ProducesResponseType<PropostaResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropostaResposta>> CriarAsync(
        NovaProposta corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chave,
        CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return this.ProblemaDeValidacao(erros);
        }

        // Repetição de um POST que já deu certo (o cliente não recebeu a resposta e tentou de novo).
        if (chave is not null && await BuscarPorChaveAsync(chave, cancellationToken) is { } existente)
        {
            return Repetida(existente);
        }

        var proposta = new PropostaSeguroVida(
            Guid.NewGuid(), corpo.ClienteId!, corpo.Idade!.Value, corpo.CapitalSegurado!.Value, corpo.Plano!, DateTimeOffset.UtcNow);
        var registro = new PropostaRegistrada
        {
            PropostaId = proposta.PropostaId,
            ClienteId = proposta.ClienteId,
            Idade = proposta.Idade,
            CapitalSegurado = proposta.CapitalSegurado,
            Plano = proposta.Plano,
            Status = StatusDaProposta.Recebida,
            RecebidaEm = proposta.CriadaEm,
            ChaveDeIdempotencia = chave,
        };
        banco.Propostas.Add(registro);
        // A mensagem é montada pela mesma fábrica do envio direto e guardada como linha do outbox.
        banco.OutboxMensagens.Add(MensagemDeSaida.De(Filas.Propostas, MensagensProposta.Criar(proposta)));

        try
        {
            // Um SaveChanges = uma transação: as duas linhas são gravadas, ou nenhuma.
            await banco.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (chave is not null && ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Corrida: outro POST com a mesma chave gravou entre a busca e o INSERT. O índice único decide.
            banco.ChangeTracker.Clear();
            return Repetida((await BuscarPorChaveAsync(chave, cancellationToken))!);
        }

        logger.LogInformation(
            "API: proposta {PropostaId} gravada com a mensagem no outbox ({Plano}, idade {Idade}). 201 sem falar com o Service Bus.",
            registro.PropostaId, registro.Plano, registro.Idade);
        // Location relativo, como antes; CreatedAtAction geraria uma URL absoluta.
        return Created($"/api/propostas/{registro.PropostaId}", PropostaResposta.De(registro));
    }

    /// <summary>
    /// GET /api/propostas/{id}: 200 com o status, ou 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EndpointName("ObterProposta")]
    [ProducesResponseType<PropostaResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropostaResposta>> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        await banco.Propostas.AsNoTracking().FirstOrDefaultAsync(p => p.PropostaId == id, cancellationToken) is { } proposta
            ? PropostaResposta.De(proposta)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Proposta não encontrada", detail: $"Nenhuma proposta com id {id}.");

    private Task<PropostaRegistrada?> BuscarPorChaveAsync(string chave, CancellationToken cancellationToken) =>
        banco.Propostas.AsNoTracking().FirstOrDefaultAsync(p => p.ChaveDeIdempotencia == chave, cancellationToken);

    private OkObjectResult Repetida(PropostaRegistrada existente)
    {
        // 200 (não 201): nada foi criado agora. O cabeçalho deixa explícito que é uma resposta repetida.
        Response.Headers["Idempotent-Replayed"] = "true";
        logger.LogInformation(
            "API: Idempotency-Key repetida. Devolvendo a proposta {PropostaId} já criada, sem gravar nem publicar de novo.",
            existente.PropostaId);
        return Ok(PropostaResposta.De(existente));
    }
}
