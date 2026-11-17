namespace Vida.Subscricao.Dados;

/// <summary>
/// Endosso pedido numa apólice (tabela Subscricao.Endossos). A versão é gerada aqui, na mesma transação que grava a
/// linha e o comando no outbox: é ela que compõe o MessageId (dedup do broker) e que o ProcessadorDeEndossos usa
/// para aplicar em ordem. Como a origem é o banco, um reenvio do mesmo pedido nunca ganha uma versão nova.
/// </summary>
public sealed class EndossoRegistrado
{
    /// <summary>Tamanho da coluna Tipo; a API valida antes de gravar.</summary>
    public const int TamanhoMaximoDoTipo = 100;

    /// <summary>Tamanho da coluna ChaveDeIdempotencia; a API valida antes de gravar.</summary>
    public const int TamanhoMaximoDaChave = 100;

    /// <summary>Parte da chave primária (ApoliceId, Versao).</summary>
    public Guid ApoliceId { get; set; }

    /// <summary>1, 2, 3... por apólice. A chave primária impede que dois pedidos simultâneos levem a mesma.</summary>
    public int Versao { get; set; }

    public required string Tipo { get; set; }

    public DateTimeOffset SolicitadoEm { get; set; }

    /// <summary>Null enquanto o ProcessadorDeEndossos não aplicou.</summary>
    public DateTimeOffset? AplicadoEm { get; set; }

    /// <summary>Idempotency-Key do POST, se o cliente mandou: repetir o pedido devolve este endosso.</summary>
    public string? ChaveDeIdempotencia { get; set; }
}
