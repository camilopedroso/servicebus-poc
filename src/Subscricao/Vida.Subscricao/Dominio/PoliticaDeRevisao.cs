namespace Vida.Subscricao.Dominio;

/// <summary>
/// Decide se uma apólice vai para revisão manual e resseguro antes de ser cobrada.
/// A regra mora aqui, num lugar só: o publisher grava o resultado na propriedade "requerRevisao"
/// e os filtros das assinaturas só roteiam por ela, sem repetir o limite de capital.
/// Regra interna da Subscrição: os outros serviços só veem o resultado, no evento ApoliceEmitida.
/// </summary>
public static class PoliticaDeRevisao
{
    /// <summary>Capital segurado acima do qual a apólice exige revisão manual (regra fictícia).</summary>
    public const decimal LimiteDeCapital = 1_000_000m;

    /// <summary>
    /// Indica se a apólice exige revisão manual.
    /// </summary>
    /// <param name="capitalSegurado">Capital segurado da apólice.</param>
    /// <returns>True se o capital passar do limite.</returns>
    public static bool RequerRevisao(decimal capitalSegurado) => capitalSegurado > LimiteDeCapital;
}
