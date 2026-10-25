namespace Vida.Producer.Contratos;

/// <summary>
/// Corpo de POST /propostas/sem-outbox.
/// </summary>
/// <param name="FalharAntesDoEnvio">Se true, simula o processo caindo entre o INSERT e o envio ao Service Bus.</param>
public sealed record EnvioSemOutboxParametros(bool FalharAntesDoEnvio = true);
