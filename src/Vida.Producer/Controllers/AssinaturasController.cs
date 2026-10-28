using Microsoft.AspNetCore.Mvc;
using Vida.Mensageria.Topologia;
using Vida.Producer.Operacao;
using Vida.Producer.Contratos;

namespace Vida.Producer.Controllers;

/// <summary>
/// Ferramentas de operação das assinaturas: regras e peek.
/// </summary>
/// <param name="inspecao">Peek e leitura das regras.</param>
[ApiController]
[Route("assinaturas")]
[Tags("Operação")]
public sealed class AssinaturasController(InspecaoDeEntidades inspecao) : ControllerBase
{
    /// <summary>
    /// GET /assinaturas/regras: lista as regras (filtros/actions) de cada assinatura.
    /// </summary>
    [HttpGet("regras")]
    public Task<RegrasDaAssinatura[]> ListarRegrasAsync(CancellationToken cancellationToken) =>
        inspecao.ListarRegrasAsync(cancellationToken);

    /// <summary>
    /// GET /assinaturas/{nome}/mensagens?dlq=: peek em uma assinatura (ou na DLQ dela).
    /// </summary>
    [HttpGet("{nome}/mensagens")]
    public async Task<ActionResult<MensagemEspiada[]>> EspiarAsync(string nome, [FromQuery] bool? dlq, CancellationToken cancellationToken)
    {
        var assinatura = Assinaturas.Interpretar(nome);
        if (assinatura is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Assinatura desconhecida",
                detail: $"'{nome}' não existe. Válidas: {string.Join(", ", Assinaturas.Todas)}.");
        }
        return await inspecao.EspiarAssinaturaAsync(assinatura, dlq ?? false, cancellationToken);
    }
}
