namespace Vida.Mensageria.Topologia;

public static class Assinaturas
{
    /// <summary>Tópico apolice-emitida. SQL filter: Subject = 'ApoliceEmitida' AND requerRevisao = FALSE.</summary>
    public const string Cobranca = "cobranca";

    /// <summary>Tópico apolice-emitida. Sem filtro (TrueFilter): recebe tudo.</summary>
    public const string Notificacao = "notificacao";

    /// <summary>Tópico apolice-emitida. SQL filter: requerRevisao = TRUE, com action que define prioridade = 'alta'.</summary>
    public const string AltoValor = "alto-valor";

    /// <summary>Tópico proposta-recusada. Sem filtro (TrueFilter): avisa o cliente de toda recusa.</summary>
    public const string NotificacaoRecusa = "notificacao-recusa";

    public static readonly string[] Todas = [Cobranca, Notificacao, AltoValor, NotificacaoRecusa];

    /// <summary>
    /// Converte o nome que veio de fora (rota HTTP, argumento de linha de comando) no nome da topologia,
    /// sem diferenciar maiúsculas de minúsculas e ignorando espaços nas pontas. Devolve null se não existir.
    /// </summary>
    /// <param name="texto">Nome como o cliente mandou: "Cobranca", "ALTO-VALOR"...</param>
    public static string? Interpretar(string? texto)
    {
        var semEspacos = texto?.Trim();
        foreach (var assinatura in Todas)
        {
            if (string.Equals(assinatura, semEspacos, StringComparison.OrdinalIgnoreCase))
            {
                return assinatura;
            }
        }
        return null;
    }

    /// <summary>
    /// Tópico ao qual a assinatura pertence. No broker o endereço de uma assinatura é sempre (tópico, assinatura).
    /// </summary>
    /// <param name="assinatura">Nome da assinatura.</param>
    /// <returns>Nome do tópico.</returns>
    public static string TopicoDe(string assinatura) => assinatura switch
    {
        NotificacaoRecusa => Topicos.PropostaRecusada,
        Cobranca or Notificacao or AltoValor => Topicos.ApoliceEmitida,
        _ => throw new ArgumentException($"Assinatura desconhecida: {assinatura}.", nameof(assinatura)),
    };
}
