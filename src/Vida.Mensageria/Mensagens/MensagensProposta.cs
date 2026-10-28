using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;

namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Contém métodos auxiliares para criar mensagens do tipo PropostaSeguroVida.
/// Usado pelo envio direto e pelo outbox (fase 4): os dois caminhos geram a mesma mensagem.
/// </summary>
public static class MensagensProposta
{
    /// <summary>
    /// Monta a mensagem da proposta para a fila "propostas".
    /// </summary>
    /// <param name="proposta">Proposta de seguro de vida.</param>
    /// <param name="simularFalha">Se true, adiciona a propriedade SimularFalha na mensagem.</param>
    /// <param name="retryComBackoff">Se true, adiciona a propriedade RetryComBackoff na mensagem.</param>
    /// <returns>Mensagem do Service Bus pronta para envio.</returns>
    public static ServiceBusMessage Criar(PropostaSeguroVida proposta, bool simularFalha = false, bool retryComBackoff = false)
    {
        var corpo = BinaryData.FromObjectAsJson(proposta);
        var mensagem = new ServiceBusMessage(corpo)
        {
            // MessageId = id de negócio: base para detecção de duplicatas.
            MessageId = proposta.PropostaId.ToString(),
            ContentType = "application/json",
            // Subject (antigo "Label") descreve o tipo da mensagem; filtros de assinatura podem usar.
            Subject = TiposDeMensagem.PropostaRecebida,
            CorrelationId = proposta.ClienteId,
        };
        mensagem.ApplicationProperties[Propriedades.Plano] = proposta.Plano.ToString();
        if (simularFalha)
        {
            mensagem.ApplicationProperties[Propriedades.SimularFalha] = true;
        }
        if (retryComBackoff)
        {
            mensagem.ApplicationProperties[Propriedades.RetryComBackoff] = true;
        }
        return mensagem;
    }
}
