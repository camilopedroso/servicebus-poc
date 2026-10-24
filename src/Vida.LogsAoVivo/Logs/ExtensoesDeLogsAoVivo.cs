using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Liga os logs do app ao painel: registra o provider e expõe GET /logs como Server-Sent Events.
/// </summary>
public static class ExtensoesDeLogsAoVivo
{
    /// <summary>
    /// Registra o CanalDeLogs e o provider de logs ao vivo.
    /// </summary>
    /// <param name="builder">Builder do app.</param>
    /// <returns>O mesmo builder, para encadear.</returns>
    public static WebApplicationBuilder AddLogsAoVivo(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<CanalDeLogs>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ProvedorDeLogsAoVivo>());
        return builder;
    }

    /// <summary>
    /// Mapeia GET /logs. SSE em vez de WebSocket: o fluxo é só servidor → painel, e o EventSource
    /// do navegador já reconecta sozinho quando o app reinicia.
    /// </summary>
    /// <param name="app">App onde o endpoint é mapeado.</param>
    /// <returns>O mesmo app, para encadear.</returns>
    public static IEndpointRouteBuilder MapLogsAoVivo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/logs", (CanalDeLogs canal, IHostApplicationLifetime ciclo, HttpContext contexto) =>
            TypedResults.ServerSentEvents(
                Acompanhar(canal, contexto.RequestAborted, ciclo.ApplicationStopping),
                eventType: "log"));
        return app;
    }

    /// <summary>
    /// Acompanha o canal até o painel desconectar ou o app começar a encerrar. Sem o segundo token,
    /// o Ctrl+C esperaria o painel fechar a conexão (até o timeout de shutdown do Kestrel).
    /// </summary>
    private static async IAsyncEnumerable<RegistroDeLog> Acompanhar(
        CanalDeLogs canal, [EnumeratorCancellation] CancellationToken desconectou, CancellationToken encerrando)
    {
        using var cancelamento = CancellationTokenSource.CreateLinkedTokenSource(desconectou, encerrando);
        var enumerador = canal.Acompanhar(cancelamento.Token).GetAsyncEnumerator(cancelamento.Token);
        try
        {
            while (true)
            {
                bool temProximo;
                try
                {
                    temProximo = await enumerador.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    // Fim normal do stream.
                    yield break;
                }
                if (!temProximo)
                {
                    yield break;
                }
                yield return enumerador.Current;
            }
        }
        finally
        {
            await enumerador.DisposeAsync();
        }
    }
}
