using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Vida.Subscricao.Erros;

/// <summary>
/// Converte as falhas do banco em ProblemDetails 503: o SQL Server caiu durante a requisição, e o cliente pode
/// tentar de novo depois. A falta de configuração não chega aqui: sem connection string, o serviço nem sobe.
/// As do Service Bus e da autenticação ficam no TratadorDeErrosDoServiceBus, da plataforma.
/// </summary>
/// <param name="problemDetails">Serviço que escreve a resposta ProblemDetails.</param>
/// <param name="logger">Logger; registra a exceção completa.</param>
public sealed class TratadorDeErrosDoBanco(IProblemDetailsService problemDetails, ILogger<TratadorDeErrosDoBanco> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excecao, CancellationToken cancellationToken)
    {
        if (excecao is not SqlException)
        {
            // Não é deste tratador: o próximo registrado (ou o padrão, com 500) responde.
            return false;
        }

        const int status = StatusCodes.Status503ServiceUnavailable;
        const string titulo = "Banco de dados indisponível";
        // O detalhe da exceção vai para o log, não para o cliente: ele não precisa (nem deve) ver o erro interno.
        const string detalhe = "Não foi possível acessar o banco da subscrição. Tente de novo em instantes.";

        logger.LogError(excecao, "{Titulo}: {Detalhe}", titulo, detalhe);
        contexto.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = excecao,
            ProblemDetails = new ProblemDetails { Status = status, Title = titulo, Detail = detalhe },
        });
    }
}
