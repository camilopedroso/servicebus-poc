using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Vida.Subscricao.Analise;

/// <summary>
/// Decide se uma exceção ao gravar a análise é transitória (vale esperar e tentar de novo) ou não (repetir dá o
/// mesmo erro). Separado do processor para testar sem banco nem Service Bus.
/// </summary>
public static class FalhasTransitorias
{
    // Números de erro do SQL Server que costumam passar sozinhos: rede/conexão, timeout, deadlock, banco
    // indisponível ou em failover, e os de limite de recursos do Azure SQL. Mesma ideia da lista do
    // SqlServerRetryingExecutionStrategy do EF Core, que é interna (não dá para reaproveitar sem warning).
    private static readonly HashSet<int> NumerosTransitorios =
    [
        -2,     // timeout do cliente
        -1,     // erro de conexão ao abrir
        2, 53,  // servidor não encontrado ou inacessível (container parado, rede)
        64, 233, 10053, 10054, 10060, 10061, // conexão derrubada, sem resposta ou recusada (porta fechada)
        1205,   // escolhido como vítima de deadlock
        4060,   // banco indisponível (ainda subindo)
        40197, 40501, 40613, 49918, 49919, 49920, // Azure SQL: failover, ocupado, limites
        10928, 10929, // Azure SQL: limite de recursos
    ];

    /// <summary>
    /// True se a exceção (ou a SqlException dentro dela) indica falha que tende a passar.
    /// </summary>
    /// <param name="excecao">Exceção lançada ao gravar.</param>
    public static bool EhTransitoria(Exception excecao) => excecao switch
    {
        SqlException sql => EhNumeroTransitorio(sql.Number) || sql.IsTransient,
        DbUpdateException { InnerException: { } interna } => EhTransitoria(interna),
        // Sem EnableRetryOnFailure, a estratégia de execução padrão do EF Core para SQL Server embrulha a falha
        // transitória num InvalidOperationException ("...likely due to a transient failure..."): a decisão é a de dentro.
        InvalidOperationException { InnerException: { } interna } => EhTransitoria(interna),
        DbException db => db.IsTransient,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>
    /// True se o número de erro do SQL Server está na lista de transitórios.
    /// </summary>
    /// <param name="numero">SqlException.Number.</param>
    public static bool EhNumeroTransitorio(int numero) => NumerosTransitorios.Contains(numero);
}
