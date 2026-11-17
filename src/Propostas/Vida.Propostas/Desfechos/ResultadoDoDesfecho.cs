namespace Vida.Propostas.Desfechos;

/// <summary>
/// O que aconteceu ao aplicar um evento de desfecho (ApoliceEmitida ou PropostaRecusada) na proposta da API.
/// </summary>
public enum ResultadoDoDesfecho
{
    /// <summary>A proposta passou para Aprovada ou Recusada.</summary>
    Aplicado,

    /// <summary>A proposta já tinha desfecho: evento repetido (at-least-once), nada muda.</summary>
    JaDecidida,

    /// <summary>
    /// A proposta não está no banco. Toda proposta é gravada antes de o outbox publicar, então isto é inconsistência
    /// (evento de outro ambiente, publicador indevido): a mensagem vai para a DLQ para alguém investigar.
    /// </summary>
    PropostaDesconhecida,
}
