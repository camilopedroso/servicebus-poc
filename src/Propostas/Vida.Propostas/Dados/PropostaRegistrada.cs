using Vida.Propostas.Contratos.Dominio;

namespace Vida.Propostas.Dados;

/// <summary>
/// Proposta gravada pela API (tabela Propostas.Registradas). É o dado de negócio; a mensagem para o Service Bus
/// vai para a tabela OutboxMensagens na mesma transação.
/// </summary>
public sealed class PropostaRegistrada
{
    public Guid PropostaId { get; set; }

    public required string ClienteId { get; set; }

    public int Idade { get; set; }

    public decimal CapitalSegurado { get; set; }

    public required Plano Plano { get; set; }

    public StatusDaProposta Status { get; set; }

    public DateTimeOffset RecebidaEm { get; set; }

    /// <summary>Quando o despachante do outbox publicou a mensagem (null enquanto não publicou).</summary>
    public DateTimeOffset? EnviadaEm { get; set; }

    /// <summary>Quando a subscrição decidiu (aprovou ou recusou); null enquanto a análise não terminou.</summary>
    public DateTimeOffset? DecididaEm { get; set; }

    /// <summary>Apólice emitida, se a proposta foi aprovada.</summary>
    public Guid? ApoliceId { get; set; }

    /// <summary>Explicação da recusa para o cliente, se a proposta foi recusada.</summary>
    public string? MotivoDaRecusa { get; set; }

    /// <summary>
    /// Valor do cabeçalho Idempotency-Key do POST, se o cliente mandou. Índice único: o mesmo POST repetido
    /// (timeout, retry do cliente) devolve a proposta já criada em vez de criar outra.
    /// </summary>
    public string? ChaveDeIdempotencia { get; set; }

    /// <summary>Tamanho da coluna ChaveDeIdempotencia; a API valida antes de gravar.</summary>
    public const int TamanhoMaximoDaChave = 100;
}
