using Vida.Subscricao.Dados;

namespace Vida.Subscricao.Contratos;

/// <summary>
/// Corpo de POST /api/apolices/{apoliceId}/endossos. A versão não vem do cliente: o servidor gera a próxima.
/// </summary>
/// <param name="Tipo">Tipo de alteração (ex.: "Troca de beneficiário").</param>
public sealed record NovoEndosso(string? Tipo)
{
    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar()
    {
        var erros = new Dictionary<string, string[]>();
        // O limite vale para o texto sem os espaços das pontas, que o controller remove antes de gravar.
        var tipo = Tipo?.Trim();
        if (string.IsNullOrEmpty(tipo) || tipo.Length > EndossoRegistrado.TamanhoMaximoDoTipo)
        {
            erros[nameof(Tipo)] = [$"Informe o tipo de endosso (até {EndossoRegistrado.TamanhoMaximoDoTipo} caracteres)."];
        }
        return erros;
    }
}
