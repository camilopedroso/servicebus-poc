using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Vida.Plataforma.Conexao;

/// <summary>
/// Fábrica para criar instâncias do ServiceBusClient, configuradas com retry e suporte a transações entre entidades.
/// </summary>
public static class ServiceBusClientFactory
{
    /// <summary>Arquivo gerado por infra/up.ps1 com o endpoint do namespace.</summary>
    public const string ArquivoConfiguracaoLocal = "servicebus.local.json";

    /// <summary>
    /// Obtém o namespace totalmente qualificado do Service Bus a partir da configuração.
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static string ObterNamespace(IConfiguration configuration) =>
        configuration["ServiceBus:FullyQualifiedNamespace"]
        ?? throw new InvalidOperationException(
            $"ServiceBus:FullyQualifiedNamespace não configurado. Rode .\\infra\\up.ps1 para gerar {ArquivoConfiguracaoLocal}.");

    /// <summary>
    /// Cria o ServiceBusClient. Ele é thread-safe e mantém a conexão AMQP:
    /// crie UM por aplicação (singleton) e reutilize, como um IConnection do RabbitMQ.
    /// </summary>
    /// <param name="transacoesEntreEntidades">
    /// Permite numa mesma transação completar uma mensagem de uma entidade e enviar para outra
    /// (ex.: completar na fila e publicar no tópico). O broker roteia o envio "via" a entidade de origem.
    /// </param>
    public static ServiceBusClient Criar(string fullyQualifiedNamespace, bool transacoesEntreEntidades = false)
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
            EnableCrossEntityTransactions = transacoesEntreEntidades,
        };

        return new ServiceBusClient(fullyQualifiedNamespace, credential, options);
    }
}
