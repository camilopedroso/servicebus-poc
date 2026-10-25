using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Mvc;
using Vida.Assinantes.Contratos;
using Vida.Assinantes.Processamento;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes.Controllers;

/// <summary>
/// Endpoints que o painel usa para ver e ligar/desligar as assinaturas deste app.
/// </summary>
/// <param name="gerenciador">Dono dos processors, um por assinatura.</param>
/// <param name="admin">Lê contagens e regras das assinaturas.</param>
[ApiController]
[Route("assinaturas")]
public sealed class AssinaturasController(GerenciadorDeAssinaturas gerenciador, ServiceBusAdministrationClient admin) : ControllerBase
{
    // As regras só mudam com um novo deploy do Bicep: lê uma vez por processo (static, porque o controller é criado a cada requisição).
    private static readonly ConcurrentDictionary<string, string[]> FiltrosEmCache = new();

    /// <summary>
    /// GET /assinaturas: estado, filtros e contagens de cada assinatura.
    /// </summary>
    [HttpGet]
    public async Task<EstadoDaAssinatura[]> ListarAsync(CancellationToken cancellationToken)
    {
        // Uma chamada de runtime properties por assinatura, em paralelo.
        var estados = gerenciador.Processadores.Select(async processador =>
        {
            var propriedades = await admin.GetSubscriptionRuntimePropertiesAsync(
                Assinaturas.TopicoDe(processador.Nome), processador.Nome, cancellationToken);
            return new EstadoDaAssinatura(
                processador.Nome,
                await ObterFiltrosAsync(processador.Nome, cancellationToken),
                processador.Ativa,
                propriedades.Value.ActiveMessageCount,
                propriedades.Value.DeadLetterMessageCount);
        });
        return await Task.WhenAll(estados);
    }

    /// <summary>
    /// POST /assinaturas/{nome}/iniciar: liga o processor da assinatura.
    /// </summary>
    [HttpPost("{nome}/iniciar")]
    public async Task<IActionResult> IniciarAsync(string nome, CancellationToken cancellationToken)
    {
        if (!gerenciador.Existe(nome))
        {
            return AssinaturaDesconhecida(nome);
        }
        await gerenciador.IniciarAsync(nome, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// POST /assinaturas/{nome}/parar: desliga o processor; as mensagens ficam retidas na assinatura.
    /// </summary>
    [HttpPost("{nome}/parar")]
    public async Task<IActionResult> PararAsync(string nome, CancellationToken cancellationToken)
    {
        if (!gerenciador.Existe(nome))
        {
            return AssinaturaDesconhecida(nome);
        }
        await gerenciador.PararAsync(nome, cancellationToken);
        return NoContent();
    }

    private async Task<string[]> ObterFiltrosAsync(string assinatura, CancellationToken cancellationToken)
    {
        if (FiltrosEmCache.TryGetValue(assinatura, out var emCache))
        {
            return emCache;
        }

        var filtros = new List<string>();
        await foreach (var regra in admin.GetRulesAsync(Assinaturas.TopicoDe(assinatura), assinatura, cancellationToken))
        {
            var acao = DescricaoDeRegras.DescreverAcao(regra);
            filtros.Add(acao is null
                ? DescricaoDeRegras.DescreverFiltro(regra)
                : $"{DescricaoDeRegras.DescreverFiltro(regra)} | action: {acao}");
        }
        return FiltrosEmCache[assinatura] = [.. filtros];
    }

    private ObjectResult AssinaturaDesconhecida(string nome) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Assinatura desconhecida",
        detail: $"'{nome}' não existe. Válidas: {string.Join(", ", Assinaturas.Todas)}.");
}
