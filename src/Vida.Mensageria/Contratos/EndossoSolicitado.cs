namespace Vida.Mensageria.Contratos;

/// <summary>
/// Pedido de alteração numa apólice já emitida (troca de beneficiário, aumento de capital...).
/// As versões de uma mesma apólice precisam ser aplicadas em ordem: a 3 pressupõe a 2.
/// </summary>
/// <param name="ApoliceId">Apólice alterada. Vira o SessionId da mensagem.</param>
/// <param name="Versao">Ordem do endosso dentro da apólice, começando em 1.</param>
/// <param name="Tipo">Tipo de alteração (ex.: "Troca de beneficiário").</param>
/// <param name="SolicitadoEm">Quando o endosso foi pedido.</param>
public sealed record EndossoSolicitado(
    Guid ApoliceId,
    int Versao,
    string Tipo,
    DateTimeOffset SolicitadoEm);
