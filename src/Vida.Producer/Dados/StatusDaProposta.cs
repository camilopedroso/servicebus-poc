namespace Vida.Producer.Dados;

/// <summary>
/// Situação da proposta do ponto de vista da API. Gravado como texto no banco (legível numa consulta SQL).
/// </summary>
public enum StatusDaProposta
{
    /// <summary>Gravada no banco; a mensagem está no outbox esperando o despachante.</summary>
    Recebida,

    /// <summary>O despachante publicou a mensagem na fila "propostas".</summary>
    EnviadaParaAnalise,

    /// <summary>A subscrição aprovou e emitiu a apólice (evento ApoliceEmitida).</summary>
    Aprovada,

    /// <summary>A subscrição recusou por regra de negócio (evento PropostaRecusada).</summary>
    Recusada,
}
