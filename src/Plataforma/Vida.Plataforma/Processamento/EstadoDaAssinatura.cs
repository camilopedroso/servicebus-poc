namespace Vida.Plataforma.Processamento;

/// <summary>
/// Estado de uma assinatura como GET /assinaturas devolve.
/// </summary>
/// <param name="Nome">Nome da assinatura.</param>
/// <param name="Filtros">Filtro (e action, se houver) de cada regra, lidos do broker.</param>
/// <param name="Topico">Tópico da assinatura.</param>
/// <param name="Ativa">Se o processor deste app está recebendo mensagens dela.</param>
/// <param name="Pendentes">Mensagens ativas esperando na assinatura (ActiveMessageCount).</param>
/// <param name="NaDlq">Mensagens na dead-letter queue da assinatura.</param>
public sealed record EstadoDaAssinatura(string Nome, string Topico, string[] Filtros, bool Ativa, long Pendentes, long NaDlq);
