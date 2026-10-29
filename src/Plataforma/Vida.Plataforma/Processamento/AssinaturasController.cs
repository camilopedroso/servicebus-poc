using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Mvc;
using Vida.Plataforma.Topologia;

namespace Vida.Plataforma.Processamento;

/// <summary>
/// GET /assinaturas: as assinaturas que ESTE app lê, com filtros, contagens e se estão ligadas. Cada serviço só
/// mostra as suas: a lista sai dos processors registrados, não de uma topologia global.
/// Ligar e desligar é pelo ProcessadoresController (POST /processadores/{nome}/iniciar|parar).
/// Registrado por AddProcessadoresControlaveis().
/// </summary>
/// <param name="gerenciador">Dono dos processors do app.</param>
/// <param name="admin">Lê contagens e regras das assinaturas.</param>
[ApiController]
[Route("assinaturas")]
public sealed class AssinaturasController(GerenciadorDeProcessadores gerenciador, ServiceBusAdministrationClient admin) : ControllerBase
{
    // As regras só mudam com um novo deploy do Bicep: lê uma vez por processo (static, porque o controller é criado a cada requisição).
    private static readonly ConcurrentDictionary<string, string[]> FiltrosEmCache = new();

    /// <summary>
    /// GET /assinaturas: estado, filtros e contagens de cada assinatura lida por este app.
    /// </summary>
    [HttpGet]
    public async Task<EstadoDaAssinatura[]> ListarAsync(CancellationToken cancellationToken)
    {
        // Uma chamada de runtime properties por assinatura, em paralelo: dispara todas e só depois espera.
        var estados = new List<Task<EstadoDaAssinatura>>();
        foreach (var processador in gerenciador.Processadores)
        {
            if (processador.Topico is null || processador.Assinatura is null)
            {
                // Processor de fila: não é assinatura.
                continue;
            }
            var estado = ObterEstadoAsync(processador, processador.Topico, processador.Assinatura, cancellationToken);
            estados.Add(estado);
        }
        return await Task.WhenAll(estados);
    }

    private async Task<EstadoDaAssinatura> ObterEstadoAsync(
        ProcessadorControlavel processador, string topico, string assinatura, CancellationToken cancellationToken)
    {
        var propriedades = await admin.GetSubscriptionRuntimePropertiesAsync(topico, assinatura, cancellationToken);
        var filtros = await ObterFiltrosAsync(topico, assinatura, cancellationToken);
        return new EstadoDaAssinatura(
            assinatura,
            topico,
            filtros,
            processador.Ativo,
            propriedades.Value.ActiveMessageCount,
            propriedades.Value.DeadLetterMessageCount);
    }

    private async Task<string[]> ObterFiltrosAsync(string topico, string assinatura, CancellationToken cancellationToken)
    {
        var chave = $"{topico}/{assinatura}";
        if (FiltrosEmCache.TryGetValue(chave, out var emCache))
        {
            return emCache;
        }

        var regras = admin.GetRulesAsync(topico, assinatura, cancellationToken);
        var filtros = new List<string>();
        await foreach (var regra in regras)
        {
            var filtro = DescricaoDeRegras.DescreverFiltro(regra);
            var acao = DescricaoDeRegras.DescreverAcao(regra);
            var linha = acao is null ? filtro : $"{filtro} | action: {acao}";
            filtros.Add(linha);
        }

        return FiltrosEmCache[chave] = [.. filtros];
    }
}
