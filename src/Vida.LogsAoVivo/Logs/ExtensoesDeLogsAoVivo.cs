using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Liga os logs do app ao painel: registra o provider e o controller de GET /logs (Server-Sent Events).
/// </summary>
public static class ExtensoesDeLogsAoVivo
{
    /// <summary>
    /// Registra o CanalDeLogs, o provider de logs ao vivo e o LogsController. O app precisa chamar app.MapControllers().
    /// </summary>
    /// <param name="builder">Builder do app.</param>
    /// <returns>O mesmo builder, para encadear.</returns>
    public static WebApplicationBuilder AddLogsAoVivo(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<CanalDeLogs>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ProvedorDeLogsAoVivo>());
        // O controller mora nesta biblioteca, não no app: o application part explícito garante que o MVC o encontre.
        builder.Services.AddControllers().AddApplicationPart(typeof(LogsController).Assembly);
        return builder;
    }
}
