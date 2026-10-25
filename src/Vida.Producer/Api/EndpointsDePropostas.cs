using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;

namespace Vida.Producer.Api;

/// <summary>
/// Fase 4: API REST de propostas. O POST grava a proposta e a mensagem no outbox numa única transação
/// do SQL Server e responde 201 sem falar com o Service Bus: quem publica é o DespachanteDoOutbox.
/// </summary>
public static class EndpointsDePropostas
{
    /// <summary>Cabeçalho que torna o POST seguro para repetir (mesma chave = mesma proposta).</summary>
    public const string CabecalhoDeIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Mapeia /api/propostas.
    /// </summary>
    /// <param name="app">App onde os endpoints são mapeados.</param>
    /// <returns>O mesmo app, para encadear.</returns>
    public static IEndpointRouteBuilder MapApiDePropostas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/propostas").WithTags("Propostas");
        grupo.MapPost("/", CriarAsync).WithName("CriarProposta");
        grupo.MapGet("/{id:guid}", ObterAsync).WithName("ObterProposta");
        return app;
    }

    /// <summary>
    /// POST /api/propostas: 201 Created com Location; 200 se a Idempotency-Key já foi usada; 400 se inválida.
    /// </summary>
    private static async Task<Results<Created<PropostaResposta>, Ok<PropostaResposta>, ValidationProblem>> CriarAsync(
        NovaProposta corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chave,
        BancoDePropostas banco,
        HttpContext contexto,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return TypedResults.ValidationProblem(erros);
        }
        var logger = loggers.CreateLogger("Vida.Producer.Api.ApiDePropostas");

        // Repetição de um POST que já deu certo (o cliente não recebeu a resposta e tentou de novo).
        if (chave is not null && await BuscarPorChaveAsync(banco, chave, cancellationToken) is { } existente)
        {
            return Repetida(existente, contexto, logger);
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
            return Repetida((await BuscarPorChaveAsync(banco, chave, cancellationToken))!, contexto, logger);
        }

        logger.LogInformation(
            "API: proposta {PropostaId} gravada com a mensagem no outbox ({Plano}, idade {Idade}). 201 sem falar com o Service Bus.",
            registro.PropostaId, registro.Plano, registro.Idade);
        return TypedResults.Created($"/api/propostas/{registro.PropostaId}", PropostaResposta.De(registro));
    }

    /// <summary>
    /// GET /api/propostas/{id}: 200 com o status, ou 404.
    /// </summary>
    private static async Task<Results<Ok<PropostaResposta>, ProblemHttpResult>> ObterAsync(
        Guid id, BancoDePropostas banco, CancellationToken cancellationToken) =>
        await banco.Propostas.AsNoTracking().FirstOrDefaultAsync(p => p.PropostaId == id, cancellationToken) is { } proposta
            ? TypedResults.Ok(PropostaResposta.De(proposta))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Proposta não encontrada", detail: $"Nenhuma proposta com id {id}.");

    private static Task<PropostaRegistrada?> BuscarPorChaveAsync(BancoDePropostas banco, string chave, CancellationToken cancellationToken) =>
        banco.Propostas.AsNoTracking().FirstOrDefaultAsync(p => p.ChaveDeIdempotencia == chave, cancellationToken);

    private static Ok<PropostaResposta> Repetida(PropostaRegistrada existente, HttpContext contexto, ILogger logger)
    {
        // 200 (não 201): nada foi criado agora. O cabeçalho deixa explícito que é uma resposta repetida.
        contexto.Response.Headers["Idempotent-Replayed"] = "true";
        logger.LogInformation(
            "API: Idempotency-Key repetida. Devolvendo a proposta {PropostaId} já criada, sem gravar nem publicar de novo.",
            existente.PropostaId);
        return TypedResults.Ok(PropostaResposta.De(existente));
    }
}
