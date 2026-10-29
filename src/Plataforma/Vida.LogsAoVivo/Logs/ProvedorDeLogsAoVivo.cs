using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Provider registrado ao lado do console: o mesmo log vai para o terminal e para o painel.
/// O alias permite filtrar só ele no appsettings (Logging:AoVivo:LogLevel).
/// </summary>
/// <param name="canal">Canal que distribui os registros.</param>
[ProviderAlias("AoVivo")]
public sealed class ProvedorDeLogsAoVivo(CanalDeLogs canal) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, LoggerAoVivo> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, nome => new LoggerAoVivo(nome, canal));

    public void Dispose() => _loggers.Clear();
}
