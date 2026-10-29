using Azure.Messaging.ServiceBus.Administration;

namespace Vida.Plataforma.Topologia;

/// <summary>
/// Traduz as regras de uma assinatura (filtro + action) lidas do broker para texto legível.
/// </summary>
public static class DescricaoDeRegras
{
    /// <summary>
    /// Descreve o filtro de uma regra.
    /// </summary>
    /// <param name="regra">Regra lida pelo ServiceBusAdministrationClient.</param>
    /// <returns>Descrição do filtro, ex.: "SQL: capitalSegurado &gt; 1000000".</returns>
    public static string DescreverFiltro(RuleProperties regra) => regra.Filter switch
    {
        TrueRuleFilter => "TrueFilter (tudo)",
        SqlRuleFilter sql => $"SQL: {sql.SqlExpression}",
        CorrelationRuleFilter c => $"Correlation: {DescreverCorrelacao(c)}",
        _ => regra.Filter.ToString() ?? "",
    };

    /// <summary>
    /// Descreve a action de uma regra, se houver.
    /// </summary>
    /// <param name="regra">Regra lida pelo ServiceBusAdministrationClient.</param>
    /// <returns>Expressão SQL da action, ou null se a regra não tiver action.</returns>
    public static string? DescreverAcao(RuleProperties regra) =>
        regra.Action is SqlRuleAction a ? a.SqlExpression : null;

    /// <summary>
    /// Descreve um filtro de correlação.
    /// </summary>
    /// <param name="c">Filtro de correlação.</param>
    /// <returns>Descrição legível do filtro.</returns>
    private static string DescreverCorrelacao(CorrelationRuleFilter c)
    {
        var partes = new List<string>();
        if (c.Subject is not null) partes.Add($"Subject = '{c.Subject}'");
        if (c.CorrelationId is not null) partes.Add($"CorrelationId = '{c.CorrelationId}'");
        partes.AddRange(c.ApplicationProperties.Select(p => $"{p.Key} = '{p.Value}'"));
        return string.Join(" AND ", partes);
    }
}
