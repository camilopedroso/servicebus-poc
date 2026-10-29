namespace Vida.Propostas.Contratos.Dominio;

/// <summary>
/// Operações sobre os planos de seguro de vida. O filtro SQL de uma assinatura pode rotear por eles (propriedade "plano").
/// </summary>
public static class Planos
{
    public static readonly Plano[] Todos = Enum.GetValues<Plano>();

    /// <summary>Sorteia um plano, para gerar propostas e apólices de teste.</summary>
    public static Plano Sortear()
    {
        var sorteados = Random.Shared.GetItems(Todos, 1);
        return sorteados[0];
    }

    /// <summary>
    /// Converte o texto que veio de fora (corpo de uma requisição) no plano, sem diferenciar maiúsculas de minúsculas
    /// e ignorando espaços nas pontas. Devolve null se o texto não for um plano.
    /// Não usa Enum.TryParse: ele aceita números ("7", mesmo fora do enum) e listas ("Individual, Familiar").
    /// </summary>
    /// <param name="texto">Plano como o cliente mandou: "familiar", "FAMILIAR", " Familiar "...</param>
    public static Plano? Interpretar(string? texto)
    {
        var semEspacos = texto?.Trim();
        foreach (var plano in Todos)
        {
            var nome = plano.ToString();
            if (string.Equals(nome, semEspacos, StringComparison.OrdinalIgnoreCase))
            {
                return plano;
            }
        }
        return null;
    }
}
