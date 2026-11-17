using Vida.Subscricao.Dados;

namespace Vida.Subscricao.Contratos;

/// <summary>
/// Resposta de GET /api/apolices/{apoliceId}: a apólice, a última versão de endosso aplicada e os endossos pedidos.
/// </summary>
/// <param name="ApoliceId">Id da apólice.</param>
/// <param name="PropostaId">Proposta que originou a apólice.</param>
/// <param name="ClienteId">Cliente.</param>
/// <param name="Plano">Individual, Familiar ou Empresarial.</param>
/// <param name="CapitalSegurado">Capital segurado em reais.</param>
/// <param name="PremioMensal">Prêmio mensal em reais.</param>
/// <param name="RequerRevisao">Se a apólice foi para revisão de resseguro (alto valor).</param>
/// <param name="EmitidaEm">Quando a apólice foi emitida.</param>
/// <param name="VersaoDosEndossos">Última versão de endosso aplicada (0 = nenhuma).</param>
/// <param name="Endossos">Endossos pedidos, em ordem de versão.</param>
public sealed record ApoliceResposta(
    Guid ApoliceId,
    Guid PropostaId,
    string ClienteId,
    string Plano,
    decimal CapitalSegurado,
    decimal PremioMensal,
    bool RequerRevisao,
    DateTimeOffset EmitidaEm,
    int VersaoDosEndossos,
    EndossoResposta[] Endossos)
{
    /// <summary>Resposta a partir das linhas do banco.</summary>
    public static ApoliceResposta De(ApoliceRegistrada apolice, IEnumerable<EndossoRegistrado> endossos)
    {
        var respostas = endossos.Select(EndossoResposta.De);
        return new(
            apolice.ApoliceId,
            apolice.PropostaId,
            apolice.ClienteId,
            apolice.Plano.ToString(),
            apolice.CapitalSegurado,
            apolice.PremioMensal,
            apolice.RequerRevisao,
            apolice.EmitidaEm,
            apolice.VersaoDosEndossos,
            [.. respostas]);
    }
}
