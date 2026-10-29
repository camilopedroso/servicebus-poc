namespace Vida.Propostas.Contratos.Demonstracao;

/// <summary>
/// Corpo de POST /demo/propostas/sem-outbox.
/// </summary>
/// <param name="FalharAntesDoEnvio">Se true, simula o processo caindo entre o INSERT e o envio ao Service Bus.</param>
public sealed record EnvioSemOutboxParametros(bool FalharAntesDoEnvio = true);
