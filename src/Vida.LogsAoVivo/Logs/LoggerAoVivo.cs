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

    private readonly string _categoriaCurta = categoria[(categoria.LastIndexOf('.') + 1)..];

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

        canal.Publicar(new RegistroDeLog(
            Processo: canal.Processo,
            Sequencia: 0, // atribuída pelo canal ao publicar
            Momento: DateTimeOffset.Now,
            Nivel: logLevel.ToString(),
            Categoria: _categoriaCurta,
            Mensagem: formatter(state, exception),
            Excecao: exception is null ? null : $"{exception.GetType().Name}: {exception.Message}",
            Ids: ids));
    }
}
