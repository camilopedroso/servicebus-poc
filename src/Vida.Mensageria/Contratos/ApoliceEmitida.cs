using Vida.Mensageria.Dominio;

namespace Vida.Mensageria.Contratos;

/// <summary>
/// Evento publicado no tópico "apolice-emitida" quando a subscrição aprova uma proposta.
/// Fila carrega comandos ("analise esta proposta"); tópico carrega fatos ("a apólice foi emitida").
/// </summary>
/// <param name="ApoliceId">Identificador da apólice.</param>
/// <param name="PropostaId">Identificador da proposta aprovada.</param>
/// <param name="ClienteId">Identificador do cliente.</param>
/// <param name="Plano">Plano de seguro contratado.</param>
/// <param name="CapitalSegurado">Capital segurado da apólice.</param>
/// <param name="PremioMensal">Prêmio mensal da apólice.</param>
/// <param name="EmitidaEm">Data e hora em que a apólice foi emitida.</param>
/// <param name="RequerRevisao">True se a apólice vai para revisão manual e resseguro antes da cobrança (PoliticaDeRevisao).</param>
public sealed record ApoliceEmitida(
    Guid ApoliceId,
    Guid PropostaId,
    string ClienteId,
    Plano Plano,
    decimal CapitalSegurado,
    decimal PremioMensal,
    DateTimeOffset EmitidaEm,
    bool RequerRevisao);
