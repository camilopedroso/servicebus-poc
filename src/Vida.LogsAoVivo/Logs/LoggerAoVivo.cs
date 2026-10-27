using Microsoft.Extensions.Logging;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Logger que transforma cada chamada de log em um RegistroDeLog e publica no CanalDeLogs.
/// O filtro de nível (appsettings, seção Logging) é aplicado pelo LoggerFactory antes de chegar aqui.
/// </summary>
/// <param name="categoria">Categoria do logger (nome completo da classe).</param>
/// <param name="canal">Canal que distribui os registros.</param>
public sealed class LoggerAoVivo(string categoria, CanalDeLogs canal) : ILogger
{
    // Placeholders que o painel usa para ligar uma linha de uma coluna às linhas das outras.
    private static readonly HashSet<string> NomesDeIds = ["PropostaId", "ApoliceId", "MessageId"];

    private readonly string _categoriaCurta = SemNamespace(categoria);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Logs estruturados ("Proposta {PropostaId} aprovada") chegam como pares nome/valor:
        // é daí que saem os ids, sem precisar procurar GUIDs no texto.
        var ids = new Dictionary<string, string>();
        if (state is IReadOnlyList<KeyValuePair<string, object?>> propriedades)
        {
            foreach (var (nome, valor) in propriedades)
            {
                if (NomesDeIds.Contains(nome) && valor?.ToString() is { Length: > 0 } texto)
                {
                    ids[nome] = texto;
                }
            }
        }

        var nivel = logLevel.ToString();
        var mensagem = formatter(state, exception);
        var excecao = exception is null ? null : $"{exception.GetType().Name}: {exception.Message}";
        var atividade = System.Diagnostics.Activity.Current;
        var traceId = atividade?.TraceId.ToHexString();

        var registro = new RegistroDeLog(
            Processo: canal.Processo,
            Sequencia: 0, // atribuída pelo canal ao publicar
            Momento: DateTimeOffset.Now,
            Nivel: nivel,
            Categoria: _categoriaCurta,
            Mensagem: mensagem,
            Excecao: excecao,
            Ids: ids,
            TraceId: traceId);
        canal.Publicar(registro);
    }

    private static string SemNamespace(string categoria)
    {
        var ultimoPonto = categoria.LastIndexOf('.');
        return categoria[(ultimoPonto + 1)..];
    }
}
