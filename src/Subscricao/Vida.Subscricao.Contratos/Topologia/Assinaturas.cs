namespace Vida.Subscricao.Contratos.Topologia;

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

    /// <summary>Tópico apolice-emitida. Sem filtro: o Producer marca a proposta como Aprovada.</summary>
    public const string StatusAprovada = "status-aprovada";

    /// <summary>Tópico proposta-recusada. Sem filtro: o Producer marca a proposta como Recusada.</summary>
    public const string StatusRecusada = "status-recusada";

    /// <summary>Assinaturas que o Vida.Assinantes processa (e liga/desliga pelo painel).</summary>
    public static readonly string[] DoAssinantes = [Cobranca, Notificacao, AltoValor, NotificacaoRecusa];

    /// <summary>Assinaturas que o Vida.Producer processa para atualizar o status das propostas da API.</summary>
    public static readonly string[] DoProducer = [StatusAprovada, StatusRecusada];

    /// <summary>Todas as assinaturas da topologia, para as ferramentas de operação (peek, regras).</summary>
    public static readonly string[] Todas = [.. DoAssinantes, .. DoProducer];

    /// <summary>
    /// Converte o nome que veio de fora (rota HTTP, argumento de linha de comando) no nome da topologia,
    /// sem diferenciar maiúsculas de minúsculas e ignorando espaços nas pontas. Devolve null se não existir.
    /// </summary>
    /// <param name="texto">Nome como o cliente mandou: "Cobranca", "ALTO-VALOR"...</param>
    public static string? Interpretar(string? texto) => Interpretar(texto, Todas);

    /// <summary>
    /// Como <see cref="Interpretar(string?)"/>, mas só entre as assinaturas informadas.
    /// </summary>
    /// <param name="texto">Nome como o cliente mandou.</param>
    /// <param name="candidatas">Assinaturas aceitas (ex.: <see cref="DoAssinantes"/>).</param>
    public static string? Interpretar(string? texto, string[] candidatas)
    {
        var semEspacos = texto?.Trim();
        foreach (var assinatura in candidatas)
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
        NotificacaoRecusa or StatusRecusada => Topicos.PropostaRecusada,
        Cobranca or Notificacao or AltoValor or StatusAprovada => Topicos.ApoliceEmitida,
        _ => throw new ArgumentException($"Assinatura desconhecida: {assinatura}.", nameof(assinatura)),
    };
}
