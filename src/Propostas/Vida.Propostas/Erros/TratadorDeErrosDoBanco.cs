using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Vida.Propostas.Dados;

namespace Vida.Propostas.Erros;

/// <summary>
/// Converte as falhas do banco (SQL Server local, fase 4) em ProblemDetails com uma explicação útil.
/// As do Service Bus e da autenticação ficam no TratadorDeErrosDoServiceBus, da plataforma.
/// </summary>
/// <param name="problemDetails">Serviço que escreve a resposta ProblemDetails.</param>
/// <param name="logger">Logger; o erro também aparece na coluna de Propostas.</param>
public sealed class TratadorDeErrosDoBanco(IProblemDetailsService problemDetails, ILogger<TratadorDeErrosDoBanco> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excecao, CancellationToken cancellationToken)
    {
        var (status, titulo, detalhe) = excecao switch
        {
            // Fase 4: a API de propostas depende do SQL Server local.
            BancoNaoConfiguradoException ex => (StatusCodes.Status503ServiceUnavailable, "Banco não configurado", ex.Message),
            SqlException ex => (StatusCodes.Status503ServiceUnavailable, "SQL Server indisponível",
                $"Rode infra\\sql-up.ps1 (Docker). {ex.Message}"),
            _ => (0, "", ""),
        };
        if (status == 0)
        {
            // Não é deste tratador: o próximo registrado (ou o padrão, com 500) responde.
            return false;
        }

        logger.LogError("{Titulo}: {Detalhe}", titulo, detalhe);
        contexto.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = excecao,
            ProblemDetails = new ProblemDetails { Status = status, Title = titulo, Detail = detalhe },
        });
    }
}
