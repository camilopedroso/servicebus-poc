using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Vida.Plataforma.Api;

/// <summary>
/// Converte as falhas conhecidas do Service Bus e da autenticação em ProblemDetails com uma explicação útil.
/// Registro: builder.Services.AddExceptionHandler&lt;...&gt;();
/// um app pode registrar outros (ex.: erros de banco), e o primeiro que tratar a exceção responde.
/// </summary>
/// <param name="problemDetails">Serviço que escreve a resposta ProblemDetails.</param>
/// <param name="logger">Logger; registra o erro do app.</param>
public sealed class TratadorDeErrosDoServiceBus(IProblemDetailsService problemDetails, ILogger<TratadorDeErrosDoServiceBus> logger)
    : IExceptionHandler
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
