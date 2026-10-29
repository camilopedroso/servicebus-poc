using Vida.Propostas.Contratos.Dominio;
using Vida.Producer.Dados;

namespace Vida.Producer.Contratos;

/// <summary>
/// Resposta de POST e GET /api/propostas. Não expõe a entidade do EF (a chave de idempotência fica de fora).
/// </summary>
/// <param name="PropostaId">Identificador da proposta.</param>
/// <param name="ClienteId">Identificador do cliente.</param>
/// <param name="Idade">Idade do proponente.</param>
/// <param name="CapitalSegurado">Capital segurado em reais.</param>
/// <param name="Plano">Plano contratado.</param>
/// <param name="Status">
///     Recebida (no outbox), EnviadaParaAnalise (publicada na fila), Aprovada ou Recusada (desfecho da subscrição).
/// </param>
/// <param name="RecebidaEm">Quando a API gravou a proposta.</param>
/// <param name="EnviadaEm">Quando o outbox publicou a mensagem, se já publicou.</param>
/// <param name="DecididaEm">Quando a subscrição aprovou ou recusou, se já decidiu.</param>
/// <param name="ApoliceId">Apólice emitida, se aprovada.</param>
/// <param name="MotivoDaRecusa">Explicação da recusa, se recusada.</param>
public sealed record PropostaResposta(
    Guid PropostaId,
    string ClienteId,
    int Idade,
    decimal CapitalSegurado,
    Plano Plano,
    string Status,
    DateTimeOffset RecebidaEm,
    DateTimeOffset? EnviadaEm,
    DateTimeOffset? DecididaEm,
    Guid? ApoliceId,
    string? MotivoDaRecusa)
{
    public static PropostaResposta De(PropostaRegistrada p) => new(
        p.PropostaId,
        p.ClienteId,
        p.Idade,
        p.CapitalSegurado,
        p.Plano,
        p.Status.ToString(),
        p.RecebidaEm,
        p.EnviadaEm,
        p.DecididaEm,
        p.ApoliceId,
        p.MotivoDaRecusa);
}
