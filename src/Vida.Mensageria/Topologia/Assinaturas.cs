namespace Vida.Mensageria.Topologia;

public static class Assinaturas
{
    /// <summary>Correlation filter: Subject = 'ApoliceEmitida'.</summary>
    public const string Cobranca = "cobranca";

    /// <summary>Sem filtro (TrueFilter): recebe tudo.</summary>
    public const string Notificacao = "notificacao";

    /// <summary>SQL filter: capitalSegurado &gt; 1000000, com action que define prioridade = 'alta'.</summary>
    public const string AltoValor = "alto-valor";

    public static readonly string[] Todas = [Cobranca, Notificacao, AltoValor];
}
