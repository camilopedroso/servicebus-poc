namespace Vida.Propostas.Outbox;

/// <summary>
/// Liga e desliga uma falha simulada no envio do despachante, como se o Service Bus estivesse fora do ar.
/// Só o despachante consulta: a API continua gravando normalmente, que é o ponto do outbox.
/// </summary>
public sealed class SimulacaoDeIndisponibilidade
{
    private volatile bool _ativa;

    /// <summary>True enquanto o "Service Bus" estiver fora do ar.</summary>
    public bool Ativa
    {
        get => _ativa;
        set => _ativa = value;
    }
}
