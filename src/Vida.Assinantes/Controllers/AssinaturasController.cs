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
        // Uma chamada de runtime properties por assinatura, em paralelo: dispara todas e só depois espera.
        var estados = new List<Task<EstadoDaAssinatura>>();
        foreach (var processador in gerenciador.Processadores)
        {
            var estado = ObterEstadoAsync(processador, cancellationToken);
            estados.Add(estado);
        }
        return await Task.WhenAll(estados);
    }

    /// <summary>
    /// POST /assinaturas/{nome}/iniciar: liga o processor da assinatura.
    /// </summary>
    [HttpPost("{nome}/iniciar")]
    public async Task<IActionResult> IniciarAsync(string nome, CancellationToken cancellationToken)
    {
        var assinatura = Assinaturas.Interpretar(nome);
        if (assinatura is null)
        {
            return AssinaturaDesconhecida(nome);
        }
        await gerenciador.IniciarAsync(assinatura, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// POST /assinaturas/{nome}/parar: desliga o processor; as mensagens ficam retidas na assinatura.
    /// </summary>
    [HttpPost("{nome}/parar")]
    public async Task<IActionResult> PararAsync(string nome, CancellationToken cancellationToken)
    {
        var assinatura = Assinaturas.Interpretar(nome);
        if (assinatura is null)
        {
            return AssinaturaDesconhecida(nome);
        }
        await gerenciador.PararAsync(assinatura, cancellationToken);
        return NoContent();
    }

    private async Task<EstadoDaAssinatura> ObterEstadoAsync(ProcessadorDeAssinatura processador, CancellationToken cancellationToken)
    {
        var topico = Assinaturas.TopicoDe(processador.Nome);
        var propriedades = await admin.GetSubscriptionRuntimePropertiesAsync(topico, processador.Nome, cancellationToken);
        var filtros = await ObterFiltrosAsync(processador.Nome, cancellationToken);
        return new EstadoDaAssinatura(
            processador.Nome,
            filtros,
            processador.Ativa,
            propriedades.Value.ActiveMessageCount,
            propriedades.Value.DeadLetterMessageCount);
    }

    private async Task<string[]> ObterFiltrosAsync(string assinatura, CancellationToken cancellationToken)
    {
        if (FiltrosEmCache.TryGetValue(assinatura, out var emCache))
        {
            return emCache;
        }

        var topico = Assinaturas.TopicoDe(assinatura);
        var regras = admin.GetRulesAsync(topico, assinatura, cancellationToken);
        var filtros = new List<string>();
        await foreach (var regra in regras)
        {
            var filtro = DescricaoDeRegras.DescreverFiltro(regra);
            var acao = DescricaoDeRegras.DescreverAcao(regra);
            var linha = acao is null ? filtro : $"{filtro} | action: {acao}";
            filtros.Add(linha);
        }

        return FiltrosEmCache[assinatura] = [.. filtros];
    }

    private ObjectResult AssinaturaDesconhecida(string nome)
    {
        var validas = string.Join(", ", Assinaturas.Todas);
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Assinatura desconhecida",
            detail: $"'{nome}' não existe. Válidas: {validas}.");
    }
}
