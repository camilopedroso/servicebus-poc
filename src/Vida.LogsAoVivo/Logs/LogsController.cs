using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// GET /logs como Server-Sent Events. SSE em vez de WebSocket: o fluxo é só servidor → painel, e o EventSource
/// do navegador já reconecta sozinho quando o app reinicia. Registrado nos três apps por AddLogsAoVivo().
/// </summary>
/// <param name="canal">Histórico e assinantes dos logs deste processo.</param>
/// <param name="ciclo">Ciclo de vida do app, para encerrar o stream no Ctrl+C.</param>
[ApiController]
[Route("logs")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class LogsController(CanalDeLogs canal, IHostApplicationLifetime ciclo) : ControllerBase
{
    /// <summary>
    /// Mantém a conexão aberta e envia cada log como um evento "log".
    /// </summary>
    /// <returns>O stream SSE.</returns>
    [HttpGet]
    public IResult Acompanhar()
    {
        var registros = Acompanhar(canal, HttpContext.RequestAborted, ciclo.ApplicationStopping);
        return TypedResults.ServerSentEvents(registros, eventType: "log");
    }

    /// <summary>
    /// Acompanha o canal até o painel desconectar ou o app começar a encerrar. Sem o segundo token,
    /// o Ctrl+C esperaria o painel fechar a conexão (até o timeout de shutdown do Kestrel).
    /// </summary>
    private static async IAsyncEnumerable<RegistroDeLog> Acompanhar(
        CanalDeLogs canal, [EnumeratorCancellation] CancellationToken desconectou, CancellationToken encerrando)
    {
        using var cancelamento = CancellationTokenSource.CreateLinkedTokenSource(desconectou, encerrando);
        var registros = canal.Acompanhar(cancelamento.Token);
        var enumerador = registros.GetAsyncEnumerator(cancelamento.Token);
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
