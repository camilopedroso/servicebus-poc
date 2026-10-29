using Vida.Propostas.Contratos.Dominio;

namespace Vida.Propostas.Contratos;

/// <summary>
/// Corpo de POST /api/propostas. Diferente do POST /demo/propostas da demonstração, nada é sorteado:
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
        // O limite vale para o id sem os espaços das pontas, que o controller remove antes de gravar.
        var clienteId = ClienteId?.Trim();
        if (string.IsNullOrEmpty(clienteId) || clienteId.Length > 20)
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
        if (Planos.Interpretar(Plano) is null)
        {
            erros[nameof(Plano)] = [$"Planos válidos: {string.Join(", ", Planos.Todos)}."];
        }
        return erros;
    }
}
