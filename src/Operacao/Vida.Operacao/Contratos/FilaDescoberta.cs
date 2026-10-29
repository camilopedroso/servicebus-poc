namespace Vida.Operacao.Contratos;

/// <summary>
/// Uma fila como o broker a descreve, com as contagens de agora.
/// </summary>
/// <param name="Nome">Nome da fila.</param>
/// <param name="ComSessoes">requiresSession: só dá para ler (e espiar) por sessão.</param>
/// <param name="Ativas">Mensagens esperando consumo (ActiveMessageCount).</param>
/// <param name="NaDlq">Mensagens na dead-letter queue.</param>
/// <param name="Agendadas">Mensagens com ScheduledEnqueueTime no futuro (retry com backoff).</param>
public sealed record FilaDescoberta(string Nome, bool ComSessoes, long Ativas, long NaDlq, long Agendadas);
