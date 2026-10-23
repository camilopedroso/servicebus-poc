namespace Vida.Mensageria;

/// <summary>
/// Evento publicado no tópico "apolice-emitida" quando a subscrição aprova uma proposta.
/// Fila carrega comandos ("analise esta proposta"); tópico carrega fatos ("a apólice foi emitida").
/// </summary>
public sealed record ApoliceEmitida(
    Guid ApoliceId,
    Guid PropostaId,
    string ClienteId,
    string Plano,
    decimal CapitalSegurado,
    decimal PremioMensal,
    DateTimeOffset EmitidaEm);
