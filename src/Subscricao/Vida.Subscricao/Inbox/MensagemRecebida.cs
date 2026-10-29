namespace Vida.Subscricao.Inbox;

/// <summary>
/// Uma linha da inbox (tabela Subscricao.Inbox): o MessageId de uma mensagem já processada, gravado na MESMA transação
/// do dado de negócio e do outbox. O Service Bus entrega pelo menos uma vez (at-least-once): se a Subscrição gravar e
/// cair antes do Complete, a mensagem volta, e esta linha faz a segunda entrega virar um no-op.
/// </summary>
/// <remarks>
/// A detecção de duplicatas do broker (requiresDuplicateDetection) não resolve isto: ela barra um REENVIO do produtor
/// dentro da janela, não a REENTREGA da mesma mensagem ao consumidor depois de um lock perdido ou de um Abandon.
/// </remarks>
public sealed class MensagemRecebida
{
    /// <summary>Chave primária: a segunda inserção do mesmo id falha, mesmo com duas instâncias ao mesmo tempo.</summary>
    public required string MessageId { get; set; }

    public DateTimeOffset ProcessadaEm { get; set; }
}
