using Microsoft.AspNetCore.Mvc;
using Vida.Operacao.Contratos;
using Vida.Operacao.Ferramentas;

namespace Vida.Operacao.Controllers;

/// <summary>
/// Ferramentas de operação das filas: lista com contagens, peek e reprocessamento da DLQ. As filas são descobertas
/// no broker, então qualquer fila de qualquer squad serve.
/// </summary>
/// <param name="descoberta">Filas que existem no broker.</param>
/// <param name="inspecao">Peek na fila e na DLQ.</param>
/// <param name="reprocessamento">Devolve a DLQ para a fila.</param>
[ApiController]
[Route("filas")]
[Tags("Filas")]
public sealed class FilasController(
    DescobertaDeEntidades descoberta, InspecaoDeEntidades inspecao, ReprocessamentoDaDlq reprocessamento) : ControllerBase
{
    /// <summary>
    /// GET /filas: as filas do namespace com as contagens de agora (ativas, DLQ, agendadas).
    /// </summary>
    [HttpGet]
    public Task<FilaDescoberta[]> ListarAsync(CancellationToken cancellationToken) =>
        descoberta.ListarFilasAsync(cancellationToken);

    /// <summary>
    /// GET /filas/{nome}/mensagens?dlq=: peek na fila (ou na DLQ, com o motivo).
    /// </summary>
    [HttpGet("{nome}/mensagens")]
    public async Task<ActionResult<MensagemEspiada[]>> EspiarAsync(
        string nome, [FromQuery] bool? dlq, CancellationToken cancellationToken)
    {
        var (fila, problema) = await EncontrarAsync(nome, cancellationToken);
        if (fila is null)
        {
            return problema!;
        }
        if (fila.ComSessoes)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Fila com sessions",
                detail: $"'{fila.Nome}' exige sessions: só dá para ler por sessão. As contagens estão em GET /filas.");
        }
        return await inspecao.EspiarFilaAsync(fila.Nome, dlq ?? false, cancellationToken);
    }

    /// <summary>
    /// POST /filas/{nome}/dlq/reprocessar?removerPropriedade=: devolve a DLQ para a mesma fila (motivo permanente fica).
    /// </summary>
    /// <param name="nome">Nome da fila.</param>
    /// <param name="removerPropriedade">Application property a tirar de cada cópia antes de reenviar (opcional).</param>
    /// <param name="cancellationToken">Token de cancelamento da requisição.</param>
    [HttpPost("{nome}/dlq/reprocessar")]
    public async Task<ActionResult<ResultadoDoReprocessamento>> ReprocessarDlqAsync(
        string nome, [FromQuery] string? removerPropriedade, CancellationToken cancellationToken)
    {
        var (fila, problema) = await EncontrarAsync(nome, cancellationToken);
        if (fila is null)
        {
            return problema!;
        }
        return await reprocessamento.ReprocessarAsync(fila.Nome, removerPropriedade, cancellationToken);
    }

    private async Task<(FilaDescoberta? Fila, ObjectResult? Problema)> EncontrarAsync(string nome, CancellationToken cancellationToken)
    {
        var filas = await descoberta.ListarFilasAsync(cancellationToken);
        var nomes = filas.Select(f => f.Nome);
        var encontrado = NomesDeEntidades.Encontrar(nome, nomes);
        var fila = filas.FirstOrDefault(f => f.Nome == encontrado);
        if (fila is not null)
        {
            return (fila, null);
        }
        var validas = string.Join(", ", nomes);
        var problema = Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Fila desconhecida",
            detail: $"'{nome}' não existe no namespace. Filas: {validas}.");
        return (null, problema);
    }
}
