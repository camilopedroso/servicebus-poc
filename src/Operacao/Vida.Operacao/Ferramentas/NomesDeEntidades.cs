namespace Vida.Operacao.Ferramentas;

/// <summary>
/// Converte o nome que veio de fora (rota HTTP) no nome de uma entidade que existe no broker.
/// </summary>
public static class NomesDeEntidades
{
    /// <summary>
    /// Procura o nome entre os candidatos, sem diferenciar maiúsculas de minúsculas e ignorando espaços nas pontas.
    /// </summary>
    /// <param name="texto">Nome como o cliente mandou: "Cobranca", "ALTO-VALOR"...</param>
    /// <param name="candidatos">Nomes que o broker devolveu.</param>
    /// <returns>O nome como está no broker, ou null se não existir.</returns>
    public static string? Encontrar(string? texto, IEnumerable<string> candidatos)
    {
        var semEspacos = texto?.Trim();
        foreach (var candidato in candidatos)
        {
            if (string.Equals(candidato, semEspacos, StringComparison.OrdinalIgnoreCase))
            {
                return candidato;
            }
        }
        return null;
    }
}
