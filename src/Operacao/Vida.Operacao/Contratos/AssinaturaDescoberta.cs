namespace Vida.Operacao.Contratos;

/// <summary>
/// Uma assinatura como o broker a descreve, com as contagens de agora.
/// </summary>
/// <param name="Topico">Tópico da assinatura.</param>
/// <param name="Nome">Nome da assinatura.</param>
/// <param name="EncaminhaPara">forwardTo, se houver: as mensagens não ficam aqui, e o broker não deixa espiar.</param>
/// <param name="Ativas">Mensagens esperando consumo (ActiveMessageCount).</param>
/// <param name="NaDlq">Mensagens na dead-letter queue.</param>
public sealed record AssinaturaDescoberta(string Topico, string Nome, string? EncaminhaPara, long Ativas, long NaDlq);
