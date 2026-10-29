using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vida.Propostas.Contratos.Eventos;
using Vida.Propostas.Contratos.Dominio;
using Vida.Propostas.Contratos.Mensagens;
using Vida.Propostas.Contratos;
using Vida.Propostas.Dados;
using Vida.Propostas.Outbox;
using Vida.Plataforma.Api;

namespace Vida.Propostas.Controllers;

/// <summary>
/// API REST de propostas. 
/// O POST grava a proposta e a mensagem no outbox numa única transação
/// do SQL Server e responde 201 sem falar com o Service Bus: quem publica é o DespachanteDoOutbox.
/// </summary>
/// <param name="banco">DbContext da requisição (scoped).</param>
/// <param name="logger">Logger; aparece com o selo "API" na coluna de Propostas.</param>
[ApiController]
[Route("api/propostas")]
[Tags("Propostas")]
public sealed class PropostasController(
    BancoDePropostas banco,
    ILogger<PropostasController> logger) : ControllerBase
{
    /// <summary>
    /// Cabeçalho que torna o POST seguro para repetir (mesma chave = mesma proposta).
    /// </summary>
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
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chaveRecebida,
        CancellationToken cancellationToken)
    {
        // Cabeçalho em branco vale como ausente; espaços nas pontas não fazem parte da chave.
        var chave = string.IsNullOrWhiteSpace(chaveRecebida) ? null : chaveRecebida.Trim();

        var erros = corpo.Validar();
        // Sem esta checagem, uma chave maior que a coluna estourava no INSERT e virava 500.
        if (chave is { Length: > PropostaRegistrada.TamanhoMaximoDaChave })
        {
            erros[CabecalhoDeIdempotencia] = [$"Use no máximo {PropostaRegistrada.TamanhoMaximoDaChave} caracteres."];
        }
        if (erros.Count > 0)
        {
            return this.ProblemaDeValidacao(erros);
        }

        // Repetição de um POST que já deu certo (o cliente não recebeu a resposta e tentou de novo).
        if (chave is not null)
        {
            var existente = await BuscarPorChaveAsync(chave, cancellationToken);
            if (existente is not null)
            {
                return Repetida(existente);
            }
        }

        var proposta = new PropostaSeguroVida(
            PropostaId: Guid.NewGuid(),
            ClienteId: corpo.ClienteId!.Trim(),
            Idade: corpo.Idade!.Value,
            CapitalSegurado: corpo.CapitalSegurado!.Value,
            // Validar já garantiu que o texto é um plano; aqui ele vira o enum ("familiar" → Plano.Familiar).
            Plano: Planos.Interpretar(corpo.Plano)!.Value,
            CriadaEm: DateTimeOffset.UtcNow);

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
        var mensagem = MensagensProposta.Criar(proposta);
        var linhaDoOutbox = MensagemDeSaida.De(destino: MensagensProposta.Topico, mensagem: mensagem);
        banco.OutboxMensagens.Add(linhaDoOutbox);

        try
        {
            // Um SaveChanges = uma transação: as duas linhas são gravadas, ou nenhuma.
            await banco.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (chave is not null && ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Corrida: outro POST com a mesma chave gravou entre a busca e o INSERT. O índice único decide.
            banco.ChangeTracker.Clear();
            var vencedora = await BuscarPorChaveAsync(chave, cancellationToken);
            return Repetida(vencedora!);
        }

        logger.LogInformation(
            "API: proposta {PropostaId} gravada com a mensagem no outbox ({Plano}, idade {Idade}). 201 sem falar com o Service Bus.",
            registro.PropostaId, registro.Plano, registro.Idade);
        // Location relativo, como antes; CreatedAtAction geraria uma URL absoluta.
        var resposta = PropostaResposta.De(registro);
        return Created($"/api/propostas/{registro.PropostaId}", resposta);
    }

    /// <summary>
    /// GET /api/propostas/{id}: 200 com o status, ou 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EndpointName("ObterProposta")]
    [ProducesResponseType<PropostaResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropostaResposta>> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        var somenteLeitura = banco.Propostas.AsNoTracking();
        var proposta = await somenteLeitura.FirstOrDefaultAsync(p => p.PropostaId == id, cancellationToken);
        if (proposta is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Proposta não encontrada", detail: $"Nenhuma proposta com id {id}.");
        }
        return PropostaResposta.De(proposta);
    }

    private Task<PropostaRegistrada?> BuscarPorChaveAsync(string chave, CancellationToken cancellationToken)
    {
        var somenteLeitura = banco.Propostas.AsNoTracking();
        return somenteLeitura.FirstOrDefaultAsync(p => p.ChaveDeIdempotencia == chave, cancellationToken);
    }

    private OkObjectResult Repetida(PropostaRegistrada existente)
    {
        // 200 (não 201): nada foi criado agora. O cabeçalho deixa explícito que é uma resposta repetida.
        Response.Headers["Idempotent-Replayed"] = "true";
        logger.LogInformation(
            "API: Idempotency-Key repetida. Devolvendo a proposta {PropostaId} já criada, sem gravar nem publicar de novo.",
            existente.PropostaId);
        var resposta = PropostaResposta.De(existente);
        return Ok(resposta);
    }
}
