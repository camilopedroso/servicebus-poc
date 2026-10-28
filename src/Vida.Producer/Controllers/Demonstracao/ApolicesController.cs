using Microsoft.AspNetCore.Mvc;
using Vida.Mensageria.Contratos;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos.Demonstracao;

namespace Vida.Producer.Controllers.Demonstracao;

/// <summary>
/// Ppublica uma ApoliceEmitida direto no tópico "apolice-emitida", sem passar pela fila.
/// </summary>
/// <param name="publicacao">Publicação no tópico.</param>
[ApiController]
[Route("demo/apolices")]
[Tags("Demonstração")]
[ApiExplorerSettings(GroupName = "demo")]
public sealed class ApolicesController(PublicacaoDeApolices publicacao) : ControllerBase
{
    /// <summary>
    /// POST /demo/apolices: publica uma apólice; campos nulos são sorteados.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApoliceEmitida>> PublicarAsync(PublicacaoDeApolice corpo, CancellationToken cancellationToken)
    {
        if (corpo.Validar() is { Count: > 0 } erros)
        {
            return this.ProblemaDeValidacao(erros);
        }
        return await publicacao.PublicarAsync(corpo, cancellationToken);
    }
}
