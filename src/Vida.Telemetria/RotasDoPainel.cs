using Microsoft.AspNetCore.Http;

namespace Vida.Telemetria;

/// <summary>
/// Rotas que o painel (web/painel) chama o tempo todo e que não devem virar trace: só ruído e custo.
/// </summary>
internal static class RotasDoPainel
{
    /// <summary>SSE de logs, namespace (lido na carga) e polling das contagens das assinaturas.</summary>
    public static bool Contem(HttpRequest requisicao) =>
        requisicao.Path.StartsWithSegments("/logs")
        || requisicao.Path.StartsWithSegments("/namespace")
        || (HttpMethods.IsGet(requisicao.Method) && requisicao.Path.Equals("/assinaturas"));
}
