using Vida.Subscricao.Contratos.Eventos;

namespace Vida.Subscricao.Dados;

/// <summary>
/// Proposta recusada pela Subscrição por regra de negócio (tabela Subscricao.Recusas). Recusa é desfecho, não falha:
/// fica registrada como dado, e o evento PropostaRecusada sai pelo outbox.
/// </summary>
public sealed class RecusaRegistrada
{
    public Guid PropostaId { get; set; }

    public required string ClienteId { get; set; }

    public required string Motivo { get; set; }

    public DateTimeOffset RecusadaEm { get; set; }

    /// <summary>
    /// Linha a partir do evento.
    /// </summary>
    /// <param name="recusa">Evento da recusa.</param>
    /// <returns>Linha pronta para o DbContext.</returns>
    public static RecusaRegistrada De(PropostaRecusada recusa) => new()
    {
        PropostaId = recusa.PropostaId,
        ClienteId = recusa.ClienteId,
        Motivo = recusa.Motivo,
        RecusadaEm = recusa.RecusadaEm,
    };
}
