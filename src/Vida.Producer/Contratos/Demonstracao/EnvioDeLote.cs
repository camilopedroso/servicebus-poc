namespace Vida.Producer.Contratos.Demonstracao;

/// <summary>
/// Corpo de POST /demo/propostas/lote.
/// </summary>
/// <param name="Quantidade">Número de propostas válidas a enviar.</param>
public sealed record EnvioDeLote(int Quantidade = 10)
{
    public const int Maximo = 500;

    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar() =>
        Quantidade is < 1 or > Maximo
            ? new() { [nameof(Quantidade)] = [$"Informe entre 1 e {Maximo} propostas."] }
            : [];
}
