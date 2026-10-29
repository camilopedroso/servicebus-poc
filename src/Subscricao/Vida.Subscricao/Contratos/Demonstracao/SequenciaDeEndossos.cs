namespace Vida.Subscricao.Contratos.Demonstracao;

/// <summary>
/// Corpo de POST /demo/endossos/em-ordem.
/// </summary>
/// <param name="Apolices">Quantas apólices (sessões) recebem endossos.</param>
/// <param name="Versoes">Quantos endossos por apólice.</param>
public sealed record SequenciaDeEndossos(int Apolices = 3, int Versoes = 4)
{
    public const int Maximo = 20;

    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar()
    {
        var erros = new Dictionary<string, string[]>();
        if (Apolices is < 1 or > Maximo)
        {
            erros[nameof(Apolices)] = [$"Informe entre 1 e {Maximo} apólices."];
        }
        if (Versoes is < 1 or > Maximo)
        {
            erros[nameof(Versoes)] = [$"Informe entre 1 e {Maximo} versões."];
        }
        return erros;
    }
}
