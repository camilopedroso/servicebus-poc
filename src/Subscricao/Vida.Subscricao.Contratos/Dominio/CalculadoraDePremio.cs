namespace Vida.Subscricao.Contratos.Dominio;

/// <summary>
/// Regra de prêmio fictícia, só para o evento ApoliceEmitida ter um valor calculado.
/// Provisório: é regra interna da Subscrição, mas fica no contrato enquanto a demo /demo/apolices do Producer
/// também a usa. Vai para dentro do serviço na etapa 6 do plano de reestruturação, junto com a demo.
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
