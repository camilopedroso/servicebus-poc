using Microsoft.AspNetCore.Mvc;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Cenarios;
using Vida.Subscricao.Contratos.Demonstracao;
using Vida.Plataforma.Api;

namespace Vida.Subscricao.Controllers.Demonstracao;

/// <summary>
/// Publica uma ApoliceEmitida direto no tópico "apolice-emitida", sem analisar proposta nenhuma (fase 2).
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
