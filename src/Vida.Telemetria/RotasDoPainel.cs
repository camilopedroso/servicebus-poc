using Microsoft.AspNetCore.Http;

namespace Vida.Telemetria;

/// <summary>
/// Rotas que o painel (web/painel) chama o tempo todo e que não devem virar trace: só ruído e custo.
/// </summary>
internal static class RotasDoPainel
{
    /// <summary>SSE de logs, namespace (lido na carga) e polling das contagens das assinaturas.</summary>
    public static bool Contem(HttpRequest requisicao)
    {
        if (requisicao.Path.StartsWithSegments("/logs") || requisicao.Path.StartsWithSegments("/namespace"))
        {
            return true;
        }

        // O roteamento aceita "/assinaturas/" (barra no fim) para a mesma action: sem tirar a barra, ela escapava.
        var caminho = requisicao.Path.Value ?? string.Empty;
        var semBarraNoFim = caminho.TrimEnd('/');
        var ehListaDeAssinaturas = string.Equals(semBarraNoFim, "/assinaturas", StringComparison.OrdinalIgnoreCase);
        return HttpMethods.IsGet(requisicao.Method) && ehListaDeAssinaturas;
    }
}
