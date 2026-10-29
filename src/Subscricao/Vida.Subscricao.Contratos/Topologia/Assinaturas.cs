using Vida.Propostas.Contratos.Mensagens;

namespace Vida.Subscricao.Contratos.Topologia;

/// <summary>
/// Lista global das assinaturas, para as ferramentas de operação do Producer (peek, regras). Os serviços não a usam:
/// cada um declara a sua (ex.: ProcessadorDeCobranca.Assinatura). Deixa de existir na etapa 6, quando a operação
/// passa a descobrir as entidades no broker.
/// </summary>
public static class Assinaturas
{
    /// <summary>Tópico apolice-emitida, squad de Cobrança. SQL filter: Subject = 'ApoliceEmitida' AND requerRevisao = FALSE.</summary>
    public const string Cobranca = "cobranca";

    /// <summary>Tópico apolice-emitida, squad de Notificação. Sem filtro (TrueFilter): recebe tudo.</summary>
    public const string Notificacao = "notificacao";

    /// <summary>
    /// Tópico apolice-emitida, squad de Resseguro, que ainda não tem serviço: as mensagens esperam na assinatura.
    /// SQL filter: requerRevisao = TRUE, com action que define prioridade = 'alta'.
    /// </summary>
    public const string AltoValor = "alto-valor";

    /// <summary>Tópico proposta-recusada, squad de Notificação. Sem filtro (TrueFilter): avisa o cliente de toda recusa.</summary>
    public const string NotificacaoRecusa = "notificacao-recusa";

    /// <summary>Tópico apolice-emitida. Sem filtro: o Producer marca a proposta como Aprovada.</summary>
    public const string StatusAprovada = "status-aprovada";

    /// <summary>Tópico proposta-recusada. Sem filtro: o Producer marca a proposta como Recusada.</summary>
    public const string StatusRecusada = "status-recusada";

    /// <summary>
    /// Tópico proposta-recebida. Sem filtro, com forwardTo para a fila subscricao-entrada: o broker move cada
    /// proposta para a caixa de entrada da Subscrição. Ninguém lê desta assinatura.
    /// </summary>
    public const string Subscricao = "subscricao";

    /// <summary>Assinaturas que guardam mensagens, para as ferramentas de operação (peek, regras).</summary>
    public static readonly string[] Todas = [Cobranca, Notificacao, AltoValor, NotificacaoRecusa, StatusAprovada, StatusRecusada];

    /// <summary>
    /// Assinaturas com forwardTo. O broker não deixa abrir receiver nelas (nem para peek), e as mensagens nem
    /// ficam lá: só entram na leitura das regras. Se o encaminhamento falhar (destino desabilitado ou cheio), a
    /// mensagem vai para a transfer DLQ da assinatura ($Transfer/$DeadLetterQueue), não para a DLQ da fila.
    /// </summary>
    public static readonly string[] Encaminhadas = [Subscricao];

    /// <summary>
    /// Converte o nome que veio de fora (rota HTTP) no nome da topologia,
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
        Subscricao => MensagensProposta.Topico,
        NotificacaoRecusa or StatusRecusada => Topicos.PropostaRecusada,
        Cobranca or Notificacao or AltoValor or StatusAprovada => Topicos.ApoliceEmitida,
        _ => throw new ArgumentException($"Assinatura desconhecida: {assinatura}.", nameof(assinatura)),
    };
}
