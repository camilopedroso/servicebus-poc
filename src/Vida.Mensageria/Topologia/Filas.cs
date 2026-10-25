namespace Vida.Mensageria.Topologia;

/// <summary>Nomes das entidades criadas em infra/main.bicep.</summary>
public static class Filas
{
    public const string Propostas = "propostas";

    /// <summary>Fase 3. Com sessions (SessionId = ApoliceId) e detecção de duplicatas.</summary>
    public const string Endossos = "endossos";
}
