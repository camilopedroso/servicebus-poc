using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Vida.Mensageria;

/// <summary>Nomes das entidades criadas em infra/main.bicep.</summary>
public static class Filas
{
    public const string Propostas = "propostas";
}

/// <summary>Nomes de application properties (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).</summary>
public static class Propriedades
{
    /// <summary>Quando true, o consumer simula uma falha transitória (para exercitar retry → DLQ).</summary>
    public const string SimularFalha = "simularFalha";
}

public static class ServiceBusClientFactory
{
    /// <summary>Arquivo gerado por infra/up.ps1 com o endpoint do namespace.</summary>
    public const string ArquivoConfiguracaoLocal = "servicebus.local.json";

    public static string ObterNamespace(IConfiguration configuration) =>
        configuration["ServiceBus:FullyQualifiedNamespace"]
        ?? throw new InvalidOperationException(
            $"ServiceBus:FullyQualifiedNamespace não configurado. Rode .\\infra\\up.ps1 para gerar {ArquivoConfiguracaoLocal}.");

    /// <summary>
    /// Cria o ServiceBusClient. Ele é thread-safe e mantém a conexão AMQP:
    /// crie UM por aplicação (singleton) e reutilize, como um IConnection do RabbitMQ.
    /// </summary>
    public static ServiceBusClient Criar(string fullyQualifiedNamespace)
    {
        // Sem connection string: autentica via Entra ID. Localmente o DefaultAzureCredential
        // usa o login do "az login"; no Azure usaria a Managed Identity do App Service/Function/AKS.
        // O namespace tem disableLocalAuth=true, então chaves SAS nem funcionariam.
        var credential = new DefaultAzureCredential();

        var options = new ServiceBusClientOptions
        {
            // Retry do SDK para falhas transitórias de rede/servidor (não para erros do seu código).
            RetryOptions = new ServiceBusRetryOptions
            {
                Mode = ServiceBusRetryMode.Exponential,
                MaxRetries = 5,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
            },
        };

        return new ServiceBusClient(fullyQualifiedNamespace, credential, options);
    }
}
