namespace Vida.Operacao.Contratos;

/// <summary>
/// Uma mensagem vista por peek (sem travar nem consumir).
/// </summary>
/// <param name="Sequencia">SequenceNumber atribuído pelo broker.</param>
/// <param name="MessageId">Id da mensagem; nesta POC, o id de negócio (PropostaId, ApoliceId...).</param>
/// <param name="Subject">Tipo do evento.</param>
/// <param name="Entregas">DeliveryCount: quantas vezes já foi entregue a um consumer.</param>
/// <param name="Propriedades">Application properties: o que os filtros das assinaturas enxergam.</param>
/// <param name="MotivoDlq">DeadLetterReason, quando a mensagem está na DLQ.</param>
/// <param name="DescricaoDlq">DeadLetterErrorDescription, quando a mensagem está na DLQ.</param>
public sealed record MensagemEspiada(
    long Sequencia,
    string MessageId,
    string? Subject,
    int Entregas,
    IReadOnlyDictionary<string, object> Propriedades,
    string? MotivoDlq,
    string? DescricaoDlq);
