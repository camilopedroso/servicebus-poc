using Azure.Messaging.ServiceBus;

namespace Vida.Testes.Integracao.Infra;

/// <summary>
/// ServiceBusClient que não conecta em nada: o SDK expõe construtores protegidos e métodos virtuais justamente
/// para isso. Guarda o que foi "enviado" e pode simular o broker fora do ar.
/// </summary>
public sealed class ServiceBusFalso : ServiceBusClient
{
    /// <summary>Mensagens enviadas, por destino.</summary>
    public List<(string Destino, ServiceBusMessage Mensagem)> Enviadas { get; } = [];

    /// <summary>Se true, todo envio lança ServiceBusException(ServiceCommunicationProblem).</summary>
    public bool ForaDoAr { get; set; }

    public override ServiceBusSender CreateSender(string queueOrTopicName) => new SenderFalso(this, queueOrTopicName);

    private sealed class SenderFalso(ServiceBusFalso client, string destino) : ServiceBusSender
    {
        public override Task SendMessagesAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken = default)
        {
            if (client.ForaDoAr)
            {
                throw new ServiceBusException("Fora do ar (teste).", ServiceBusFailureReason.ServiceCommunicationProblem);
            }
            client.Enviadas.AddRange(messages.Select(m => (destino, m)));
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
