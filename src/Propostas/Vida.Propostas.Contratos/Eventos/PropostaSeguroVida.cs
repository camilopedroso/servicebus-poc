using Vida.Propostas.Contratos.Dominio;

namespace Vida.Propostas.Contratos.Eventos;

/// <summary>
/// Proposta de seguro de vida enviada para subscrição (análise de risco).
/// É o corpo (payload) da mensagem na fila "propostas".
/// </summary>
public sealed record PropostaSeguroVida(
    Guid PropostaId,
    string ClienteId,
    int Idade,
    decimal CapitalSegurado,
    Plano Plano,
    DateTimeOffset CriadaEm);
