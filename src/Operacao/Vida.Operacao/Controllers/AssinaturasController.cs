using Microsoft.AspNetCore.Mvc;
using Vida.Operacao.Contratos;
using Vida.Operacao.Ferramentas;

namespace Vida.Operacao.Controllers;

/// <summary>
/// Ferramentas de operação das assinaturas: lista com contagens, regras e peek. As assinaturas são descobertas no
/// broker, inclusive as que nenhum serviço consome (como alto-valor, da squad de Resseguro, que ainda não existe).
/// </summary>
/// <param name="descoberta">Assinaturas que existem no broker.</param>
/// <param name="inspecao">Peek e leitura das regras.</param>
[ApiController]
[Route("assinaturas")]
[Tags("Assinaturas")]
public sealed class AssinaturasController(DescobertaDeEntidades descoberta, InspecaoDeEntidades inspecao) : ControllerBase
{
    /// <summary>
    /// GET /assinaturas: todas as assinaturas de todos os tópicos, com contagens e forwardTo.
    /// </summary>
    [HttpGet]
    public Task<AssinaturaDescoberta[]> ListarAsync(CancellationToken cancellationToken) =>
        descoberta.ListarAssinaturasAsync(cancellationToken);

    /// <summary>
    /// GET /assinaturas/regras: lista as regras (filtros/actions) de cada assinatura.
    /// </summary>
    [HttpGet("regras")]
    public Task<RegrasDaAssinatura[]> ListarRegrasAsync(CancellationToken cancellationToken) =>
        inspecao.ListarRegrasAsync(cancellationToken);

    /// <summary>
    /// GET /assinaturas/{nome}/mensagens?dlq=: peek em uma assinatura (ou na DLQ dela). O tópico é descoberto pelo nome.
    /// </summary>
    [HttpGet("{nome}/mensagens")]
    public async Task<ActionResult<MensagemEspiada[]>> EspiarAsync(string nome, [FromQuery] bool? dlq, CancellationToken cancellationToken)
    {
        var assinaturas = await descoberta.ListarAssinaturasAsync(cancellationToken);
        var nomes = assinaturas.Select(a => a.Nome).Distinct();
        var encontrado = NomesDeEntidades.Encontrar(nome, nomes);
        var comEsteNome = assinaturas.Where(a => a.Nome == encontrado).ToArray();
        if (comEsteNome.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Assinatura desconhecida",
                detail: $"'{nome}' não existe no namespace. Assinaturas: {string.Join(", ", nomes)}.");
        }
        if (comEsteNome.Length > 1)
        {
            // O nome de uma assinatura só é único dentro do tópico; aqui o painel manda só o nome.
            var topicos = string.Join(", ", comEsteNome.Select(a => a.Topico));
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Assinatura em mais de um tópico",
                detail: $"'{encontrado}' existe nos tópicos {topicos}.");
        }
        var assinatura = comEsteNome[0];
        if (assinatura.EncaminhaPara is not null)
        {
            // O broker não deixa abrir receiver na origem de um forward, e as mensagens nem ficam lá.
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Assinatura só encaminha",
                detail: $"'{assinatura.Nome}' tem forwardTo para '{assinatura.EncaminhaPara}': espie a fila de destino.");
        }
        return await inspecao.EspiarAssinaturaAsync(assinatura.Topico, assinatura.Nome, dlq ?? false, cancellationToken);
    }
}
