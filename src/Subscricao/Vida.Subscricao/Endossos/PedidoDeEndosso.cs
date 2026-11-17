using Vida.Subscricao.Dados;

namespace Vida.Subscricao.Endossos;

/// <summary>
/// Resultado de RegistroDeEndossos.SolicitarAsync.
/// </summary>
/// <param name="Situacao">Registrado, repetido ou apólice inexistente.</param>
/// <param name="Endosso">O endosso gravado (ou o já existente); null se a apólice não existe.</param>
public sealed record PedidoDeEndosso(SituacaoDoPedidoDeEndosso Situacao, EndossoRegistrado? Endosso);
