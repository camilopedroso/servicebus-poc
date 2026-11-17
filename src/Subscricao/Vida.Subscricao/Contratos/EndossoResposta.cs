using Vida.Subscricao.Dados;

namespace Vida.Subscricao.Contratos;

/// <summary>
/// Um endosso de uma apólice, como a API devolve.
/// </summary>
/// <param name="ApoliceId">Apólice alterada.</param>
/// <param name="Versao">Ordem do endosso na apólice (1, 2, 3...), gerada pelo servidor.</param>
/// <param name="Tipo">Tipo de alteração.</param>
/// <param name="SolicitadoEm">Quando o pedido foi aceito.</param>
/// <param name="AplicadoEm">Quando foi aplicado na apólice; null enquanto espera na fila endossos.</param>
public sealed record EndossoResposta(
    Guid ApoliceId,
    int Versao,
    string Tipo,
    DateTimeOffset SolicitadoEm,
    DateTimeOffset? AplicadoEm)
{
    /// <summary>Resposta a partir da linha do banco.</summary>
    public static EndossoResposta De(EndossoRegistrado endosso) => new(
        endosso.ApoliceId,
        endosso.Versao,
        endosso.Tipo,
        endosso.SolicitadoEm,
        endosso.AplicadoEm);
}
