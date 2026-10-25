using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Vida.Producer.Dados;

namespace Vida.Producer.Erros;

/// <summary>
/// Converte as falhas conhecidas do Service Bus e da autenticação em ProblemDetails com uma explicação útil.
/// O painel mostra o "detail" logo abaixo do botão do cenário.
/// </summary>
/// <param name="problemDetails">Serviço que escreve a resposta ProblemDetails.</param>
/// <param name="logger">Logger; o erro também aparece na coluna do Producer.</param>
public sealed class TratadorDeErros(IProblemDetailsService problemDetails, ILogger<TratadorDeErros> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excecao, CancellationToken cancellationToken)
    {
        var (status, titulo, detalhe) = excecao switch
        {
            // Reason classifica o erro (ex.: MessagingEntityNotFound, ServiceBusy); IsTransient diz se vale tentar de novo.
            ServiceBusException ex => (StatusCodes.Status502BadGateway, "Erro do Service Bus",
                $"{ex.Reason} (transitório={ex.IsTransient}): {ex.Message}"),
            AuthenticationFailedException ex => (StatusCodes.Status503ServiceUnavailable, "Falha de autenticação",
                $"Rode 'az login' e tente de novo. {ex.Message}"),
            UnauthorizedAccessException ex => (StatusCodes.Status403Forbidden, "Sem permissão no namespace",
                $"O RBAC pode levar alguns minutos para propagar depois do up.ps1. {ex.Message}"),
            // Fase 4: a API de propostas depende do SQL Server local.
            BancoNaoConfiguradoException ex => (StatusCodes.Status503ServiceUnavailable, "Banco não configurado", ex.Message),
            SqlException ex => (StatusCodes.Status503ServiceUnavailable, "SQL Server indisponível",
                $"Rode infra\\sql-up.ps1 (Docker). {ex.Message}"),
            _ => (0, "", ""),
        };
        if (status == 0)
        {
            // Erro inesperado: deixa o handler padrão responder 500.
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
