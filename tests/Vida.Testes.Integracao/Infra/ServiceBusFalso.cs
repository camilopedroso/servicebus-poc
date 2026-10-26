using Azure.Messaging.ServiceBus;

namespace Vida.Testes.Integracao.Infra;

/// <summary>
/// ServiceBusClient que não conecta em nada: o SDK expõe construtores protegidos e métodos virtuais justamente
/// para isso. Guarda o que foi "enviado" e pode simular o broker fora do ar.
/// <para>
/// Não usa biblioteca de mock (Moq, NSubstitute): é herança simples. O construtor protegido sem parâmetros de
/// <see cref="ServiceBusClient"/> e de <see cref="ServiceBusSender"/> não abre conexão AMQP nem pede credencial,
/// e os métodos que o DespachanteDoOutbox usa (CreateSender, SendMessagesAsync, DisposeAsync) são virtuais.
/// Assim o despachante roda o código de produção inteiro, só com o broker trocado.
/// </para>
/// <para>
/// O banco, ao contrário, é real (<see cref="FabricaDaApi"/>): o que se quer provar no outbox é o que acontece
/// nas linhas da tabela depois do envio ou da falha. O emulador do Service Bus (módulo Testcontainers.ServiceBus)
/// permitiria testar o envio real, ao custo de mais um container e das limitações do emulador.
/// </para>
/// </summary>
public sealed class ServiceBusFalso : ServiceBusClient
{
    /// <summary>Mensagens enviadas, por destino.</summary>
    public List<(string Destino, ServiceBusMessage Mensagem)> Enviadas { get; } = [];

    /// <summary>Se true, todo envio lança ServiceBusException(ServiceCommunicationProblem).</summary>
    public bool ForaDoAr { get; set; }

    // Cada fila ou tópico ganha um sender falso que escreve na lista do client, como um sender real escreveria
    // na entidade do broker.
    public override ServiceBusSender CreateSender(string queueOrTopicName) => new SenderFalso(this, queueOrTopicName);

    private sealed class SenderFalso(ServiceBusFalso client, string destino) : ServiceBusSender
    {
        public override Task SendMessagesAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken = default)
        {
            if (client.ForaDoAr)
            {
                // A mesma exceção e o mesmo motivo que o SDK lança com o namespace inacessível: é o que o
                // despachante trata com backoff, então o teste exercita o caminho real de erro.
                throw new ServiceBusException("Fora do ar (teste).", ServiceBusFailureReason.ServiceCommunicationProblem);
            }
            client.Enviadas.AddRange(messages.Select(m => (destino, m)));
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
