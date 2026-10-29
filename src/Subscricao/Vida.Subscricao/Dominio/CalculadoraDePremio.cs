namespace Vida.Subscricao.Dominio;

/// <summary>
/// Regra de prêmio fictícia, só para o evento ApoliceEmitida ter um valor calculado.
/// Regra interna da Subscrição: os outros serviços só veem o resultado, no evento ApoliceEmitida.
/// </summary>
public static class CalculadoraDePremio
{
    /// <summary>
    /// Calcula o prêmio mensal da apólice com base no capital segurado e na idade do cliente.
    /// </summary>
    /// <param name="capitalSegurado">Capital segurado da apólice.</param>
    /// <param name="idade">Idade do cliente.</param>
    /// <returns>Prêmio mensal calculado.</returns>
    public static decimal CalcularMensal(decimal capitalSegurado, int idade) =>
        Math.Round(capitalSegurado * (0.0002m + idade * 0.00001m), 2);
}
