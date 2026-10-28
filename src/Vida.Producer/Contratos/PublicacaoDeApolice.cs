using Vida.Mensageria.Dominio;

namespace Vida.Producer.Contratos;

/// <summary>
/// Corpo de POST /apolices: publica uma ApoliceEmitida direto no tópico, sem passar pela fila. Campos nulos são sorteados.
/// </summary>
/// <param name="CapitalSegurado">Capital segurado em reais. Acima de 1.000.000 a apólice cai na assinatura alto-valor.</param>
/// <param name="Plano">Individual, Familiar ou Empresarial.</param>
public sealed record PublicacaoDeApolice(decimal? CapitalSegurado, string? Plano)
{
    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar()
    {
        var erros = new Dictionary<string, string[]>();
        if (CapitalSegurado is <= 0)
        {
            erros[nameof(CapitalSegurado)] = ["O capital segurado precisa ser maior que zero."];
        }
        if (Plano is not null && Planos.Interpretar(Plano) is null)
        {
            erros[nameof(Plano)] = [$"Planos válidos: {string.Join(", ", Planos.Todos)}."];
        }
        return erros;
    }
}
