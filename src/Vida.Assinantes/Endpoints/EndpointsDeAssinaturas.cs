using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus.Administration;
using Vida.Assinantes.Processamento;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes.Endpoints;

/// <summary>
/// Endpoints que o painel usa para ver e ligar/desligar as assinaturas deste app.
/// </summary>
public static class EndpointsDeAssinaturas
{
    // As regras só mudam com um novo deploy do Bicep: lê uma vez por processo.
    private static readonly ConcurrentDictionary<string, string[]> FiltrosEmCache = new();

    /// <summary>
    /// Mapeia GET /assinaturas e POST /assinaturas/{nome}/iniciar|parar.
    /// </summary>
    /// <param name="app">App onde os endpoints são mapeados.</param>
    /// <returns>O mesmo app, para encadear.</returns>
    public static IEndpointRouteBuilder MapAssinaturas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/assinaturas", ListarAsync);

        app.MapPost("/assinaturas/{nome}/iniciar", async (string nome, GerenciadorDeAssinaturas gerenciador, CancellationToken ct) =>
        {
            if (!gerenciador.Existe(nome)) return AssinaturaDesconhecida(nome);
            await gerenciador.IniciarAsync(nome, ct);
            return Results.NoContent();
        });

        app.MapPost("/assinaturas/{nome}/parar", async (string nome, GerenciadorDeAssinaturas gerenciador, CancellationToken ct) =>
        {
            if (!gerenciador.Existe(nome)) return AssinaturaDesconhecida(nome);
            await gerenciador.PararAsync(nome, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<EstadoDaAssinatura[]> ListarAsync(
        GerenciadorDeAssinaturas gerenciador, ServiceBusAdministrationClient admin, CancellationToken ct)
    {
        // Uma chamada de runtime properties por assinatura, em paralelo.
        var estados = gerenciador.Processadores.Select(async processador =>
        {
            var propriedades = await admin.GetSubscriptionRuntimePropertiesAsync(Topicos.ApoliceEmitida, processador.Nome, ct);
            return new EstadoDaAssinatura(
                processador.Nome,
                await ObterFiltrosAsync(admin, processador.Nome, ct),
                processador.Ativa,
                propriedades.Value.ActiveMessageCount,
                propriedades.Value.DeadLetterMessageCount);
        });
        return await Task.WhenAll(estados);
    }

    private static async Task<string[]> ObterFiltrosAsync(ServiceBusAdministrationClient admin, string assinatura, CancellationToken ct)
    {
        if (FiltrosEmCache.TryGetValue(assinatura, out var emCache))
        {
            return emCache;
        }

        var filtros = new List<string>();
        await foreach (var regra in admin.GetRulesAsync(Topicos.ApoliceEmitida, assinatura, ct))
        {
            var acao = DescricaoDeRegras.DescreverAcao(regra);
            filtros.Add(acao is null
                ? DescricaoDeRegras.DescreverFiltro(regra)
                : $"{DescricaoDeRegras.DescreverFiltro(regra)} | action: {acao}");
        }
        return FiltrosEmCache[assinatura] = [.. filtros];
    }

    private static IResult AssinaturaDesconhecida(string nome) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Assinatura desconhecida",
        detail: $"'{nome}' não existe. Válidas: {string.Join(", ", Assinaturas.Todas)}.");
}
