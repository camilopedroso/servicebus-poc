namespace Vida.Producer.Contratos;

/// <summary>
/// Resposta de GET /outbox.
/// </summary>
/// <param name="ServiceBusIndisponivel">Se a simulação de indisponibilidade está ligada.</param>
/// <param name="Pendentes">Linhas ainda não publicadas.</param>
/// <param name="Publicadas">Linhas já publicadas.</param>
public sealed record EstadoDoOutbox(bool ServiceBusIndisponivel, int Pendentes, int Publicadas);
