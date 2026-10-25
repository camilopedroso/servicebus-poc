using Vida.Mensageria.Dominio;

namespace Vida.Producer.Contratos;

/// <summary>
/// Corpo de POST /api/propostas. Diferente do POST /propostas dos cenários, nada é sorteado:
/// é a API "de verdade", e os campos de negócio são obrigatórios.
/// </summary>
/// <param name="ClienteId">Identificador do cliente.</param>
/// <param name="Idade">Idade do proponente.</param>
/// <param name="CapitalSegurado">Capital segurado em reais.</param>
/// <param name="Plano">Individual, Familiar ou Empresarial.</param>
public sealed record NovaProposta(string? ClienteId, int? Idade, decimal? CapitalSegurado, string? Plano)
{
    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar()
    {
        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(ClienteId) || ClienteId.Length > 20)
        {
            erros[nameof(ClienteId)] = ["Informe o cliente (até 20 caracteres)."];
        }
        if (Idade is null or < 0 or > 130)
        {
            erros[nameof(Idade)] = ["Informe uma idade entre 0 e 130."];
        }
        if (CapitalSegurado is null or <= 0)
        {
            erros[nameof(CapitalSegurado)] = ["Informe um capital segurado maior que zero."];
        }
        if (Plano is null || !Planos.Todos.Contains(Plano))
        {
            erros[nameof(Plano)] = [$"Planos válidos: {string.Join(", ", Planos.Todos)}."];
        }
        return erros;
    }
}
