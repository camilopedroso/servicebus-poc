namespace Vida.Consumer.Endossos;

/// <summary>
/// A regra de ordenação dos endossos, sem nada de Service Bus: função pura, testável em teste de unidade.
/// O ProcessadorDeEndossos só executa a decisão (Complete, Defer...).
/// </summary>
public static class DecisaoDeEndosso
{
    /// <summary>
    /// Decide o que fazer com a versão recebida.
    /// </summary>
    /// <param name="versao">Versão do endosso recebido.</param>
    /// <param name="ultimaVersaoAplicada">Última versão aplicada na apólice (0 = nenhuma).</param>
    /// <returns>A ação a executar.</returns>
    public static AcaoDoEndosso Decidir(int versao, int ultimaVersaoAplicada) =>
        versao <= ultimaVersaoAplicada ? AcaoDoEndosso.IgnorarJaAplicado
        : versao == ultimaVersaoAplicada + 1 ? AcaoDoEndosso.Aplicar
        : AcaoDoEndosso.Adiar;
}
