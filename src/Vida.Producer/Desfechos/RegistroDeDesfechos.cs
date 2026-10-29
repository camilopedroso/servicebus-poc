using Vida.Subscricao.Contratos.Eventos;
using Vida.Producer.Dados;

namespace Vida.Producer.Desfechos;

/// <summary>
/// Grava na proposta da API o desfecho decidido pela subscrição (Vida.Consumer). O Consumer não conhece o banco
/// da API: ele só publica o fato, e quem é dono do dado (o Producer) atualiza o seu modelo a partir do evento.
/// Separado do processor para os testes de integração aplicarem eventos sem Service Bus.
/// </summary>
public static class RegistroDeDesfechos
{
    private const int TamanhoMaximoDoMotivo = 500;

    /// <summary>
    /// ApoliceEmitida: a proposta foi aprovada e a apólice, emitida.
    /// </summary>
    /// <param name="banco">DbContext do escopo da mensagem.</param>
    /// <param name="apolice">Evento recebido da assinatura status-aprovada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task<ResultadoDoDesfecho> AplicarAprovacaoAsync(
        BancoDePropostas banco, ApoliceEmitida apolice, CancellationToken cancellationToken)
    {
        var proposta = await banco.Propostas.FindAsync([apolice.PropostaId], cancellationToken);
        var semDesfecho = VerificarSemDesfecho(proposta);
        if (semDesfecho is not null)
        {
            return semDesfecho.Value;
        }

        proposta!.Status = StatusDaProposta.Aprovada;
        proposta.ApoliceId = apolice.ApoliceId;
        proposta.DecididaEm = apolice.EmitidaEm;
        await banco.SaveChangesAsync(cancellationToken);
        return ResultadoDoDesfecho.Aplicado;
    }

    /// <summary>
    /// PropostaRecusada: a subscrição recusou por regra de negócio.
    /// </summary>
    /// <param name="banco">DbContext do escopo da mensagem.</param>
    /// <param name="recusa">Evento recebido da assinatura status-recusada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task<ResultadoDoDesfecho> AplicarRecusaAsync(
        BancoDePropostas banco, PropostaRecusada recusa, CancellationToken cancellationToken)
    {
        var proposta = await banco.Propostas.FindAsync([recusa.PropostaId], cancellationToken);
        var semDesfecho = VerificarSemDesfecho(proposta);
        if (semDesfecho is not null)
        {
            return semDesfecho.Value;
        }

        proposta!.Status = StatusDaProposta.Recusada;
        var motivo = recusa.Motivo;
        proposta.MotivoDaRecusa = motivo.Length > TamanhoMaximoDoMotivo ? motivo[..TamanhoMaximoDoMotivo] : motivo;
        proposta.DecididaEm = recusa.RecusadaEm;
        await banco.SaveChangesAsync(cancellationToken);
        return ResultadoDoDesfecho.Aplicado;
    }

    /// <summary>
    /// Null se a proposta existe e ainda espera o desfecho; senão, o motivo para não aplicar.
    /// </summary>
    private static ResultadoDoDesfecho? VerificarSemDesfecho(PropostaRegistrada? proposta)
    {
        if (proposta is null)
        {
            return ResultadoDoDesfecho.PropostaDesconhecida;
        }
        if (proposta.Status is StatusDaProposta.Aprovada or StatusDaProposta.Recusada)
        {
            return ResultadoDoDesfecho.JaDecidida;
        }
        return null;
    }
}
