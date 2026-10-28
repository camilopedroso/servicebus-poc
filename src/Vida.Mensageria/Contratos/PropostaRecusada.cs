using Vida.Mensageria.Dominio;

namespace Vida.Mensageria.Contratos;

/// <summary>
/// Evento publicado no tópico "proposta-recusada" quando a subscrição recusa uma proposta por regra de negócio.
/// A recusa é um desfecho válido (a proposta foi analisada), então a mensagem da fila é concluída, não vai para a DLQ.
/// </summary>
/// <param name="PropostaId">Identificador da proposta recusada.</param>
/// <param name="ClienteId">Identificador do cliente, para o aviso.</param>
/// <param name="Plano">Plano solicitado.</param>
/// <param name="CapitalSegurado">Capital solicitado.</param>
/// <param name="Idade">Idade do proponente.</param>
/// <param name="Motivo">Explicação da recusa, para o cliente.</param>
/// <param name="RecusadaEm">Data e hora da recusa.</param>
public sealed record PropostaRecusada(
    Guid PropostaId,
    string ClienteId,
    Plano Plano,
    decimal CapitalSegurado,
    int Idade,
    string Motivo,
    DateTimeOffset RecusadaEm);
