namespace Vida.Mensageria.Dominio;

/// <summary>
/// Planos de seguro de vida do portfólio. O filtro SQL de uma assinatura pode rotear por eles (propriedade "plano").
/// </summary>
public static class Planos
{
    public const string Individual = "Individual";
    public const string Familiar = "Familiar";
    public const string Empresarial = "Empresarial";

    public static readonly string[] Todos = [Individual, Familiar, Empresarial];

    /// <summary>Sorteia um plano, para gerar propostas e apólices de teste.</summary>
    public static string Sortear() => Random.Shared.GetItems(Todos, 1)[0];
}
