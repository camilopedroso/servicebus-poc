using Microsoft.AspNetCore.Mvc;
using Vida.Mensageria.Contratos;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos;

namespace Vida.Producer.Controllers;

/// <summary>
/// Fase 2: publica uma ApoliceEmitida direto no tópico "apolice-emitida", sem passar pela fila.
/// </summary>
/// <param name="publicacao">Publicação no tópico.</param>
[ApiController]
[Route("apolices")]
[Tags("Cenários")]
public sealed class ApolicesController(PublicacaoDeApolices publicacao) : ControllerBase
{
    /// <summary>
    /// POST /apolices: publica uma apólice; campos nulos são sorteados.
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
