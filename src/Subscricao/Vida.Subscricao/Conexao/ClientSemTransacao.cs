namespace Vida.Subscricao.Conexao;

/// <summary>
/// Chave do segundo ServiceBusClient da Subscrição, SEM transações entre entidades. Com transações, todos os links
/// do client passam pela primeira entidade tocada (a fila subscricao-entrada, a "via"), e o broker recusa abrir
/// um link em outra entidade ("Local transactions cannot span multiple top-level entities"). Por isso o client
/// transacional fica só com o ProcessadorDePropostas, e este serve ao resto: o session processor de endossos e as
/// demonstrações que publicam em apolice-emitida e enviam para endossos.
/// </summary>
public static class ClientSemTransacao
{
    /// <summary>Chave do registro keyed (AddKeyedSingleton / [FromKeyedServices]).</summary>
    public const string Chave = "sem-transacao";
}
