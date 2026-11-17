namespace Vida.Subscricao.Endossos;

/// <summary>
/// O que aconteceu com um pedido de endosso recebido pela API.
/// </summary>
public enum SituacaoDoPedidoDeEndosso
{
    /// <summary>Endosso gravado com a próxima versão e o comando no outbox.</summary>
    Registrado,

    /// <summary>A Idempotency-Key já foi usada nesta apólice: devolve o endosso existente, sem gravar outro.</summary>
    Repetido,

    /// <summary>A apólice não existe no banco da Subscrição.</summary>
    ApoliceInexistente,
}
