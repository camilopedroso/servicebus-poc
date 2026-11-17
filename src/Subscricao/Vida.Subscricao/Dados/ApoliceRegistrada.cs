using Vida.Propostas.Contratos.Dominio;
using Vida.Subscricao.Contratos.Eventos;

namespace Vida.Subscricao.Dados;

/// <summary>
/// Apólice emitida pela Subscrição (tabela Subscricao.Apolices). É o dado de negócio dela; o evento ApoliceEmitida
/// vai para a tabela OutboxMensagens na mesma transação, e a linha da inbox também.
/// </summary>
public sealed class ApoliceRegistrada
{
    public Guid ApoliceId { get; set; }

    public Guid PropostaId { get; set; }

    public required string ClienteId { get; set; }

    public required Plano Plano { get; set; }

    public decimal CapitalSegurado { get; set; }

    public decimal PremioMensal { get; set; }

    public bool RequerRevisao { get; set; }

    public DateTimeOffset EmitidaEm { get; set; }

    /// <summary>
    /// Última versão de endosso aplicada (0 = nenhum). O ProcessadorDeEndossos só avança de v - 1 para v, o que
    /// torna a aplicação idempotente: uma reentrega da mesma versão não altera nada.
    /// </summary>
    public int VersaoDosEndossos { get; set; }

    /// <summary>
    /// Linha a partir do evento: o que a Subscrição grava é exatamente o que ela publica.
    /// </summary>
    /// <param name="apolice">Evento da apólice emitida.</param>
    /// <returns>Linha pronta para o DbContext.</returns>
    public static ApoliceRegistrada De(ApoliceEmitida apolice) => new()
    {
        ApoliceId = apolice.ApoliceId,
        PropostaId = apolice.PropostaId,
        ClienteId = apolice.ClienteId,
        Plano = apolice.Plano,
        CapitalSegurado = apolice.CapitalSegurado,
        PremioMensal = apolice.PremioMensal,
        RequerRevisao = apolice.RequerRevisao,
        EmitidaEm = apolice.EmitidaEm,
    };
}
