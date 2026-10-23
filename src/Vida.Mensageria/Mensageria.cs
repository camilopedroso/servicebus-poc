using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Vida.Mensageria;

/// <summary>Nomes das entidades criadas em infra/main.bicep.</summary>
public static class Filas
{
    public const string Propostas = "propostas";
}

public static class Topicos
{
    public const string ApoliceEmitida = "apolice-emitida";
}

public static class Assinaturas
{
    /// <summary>Correlation filter: Subject = 'ApoliceEmitida'.</summary>
    public const string Cobranca = "cobranca";

    /// <summary>Sem filtro (TrueFilter): recebe tudo.</summary>
    public const string Notificacao = "notificacao";

    /// <summary>SQL filter: capitalSegurado &gt; 1000000, com action que define prioridade = 'alta'.</summary>
    public const string AltoValor = "alto-valor";

    public static readonly string[] Todas = [Cobranca, Notificacao, AltoValor];
}

/// <summary>
/// Nomes de application properties (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// Filtros de assinatura só enxergam propriedades, nunca o corpo: o que for filtrável tem que estar aqui.
/// </summary>
public static class Propriedades
{
    /// <summary>Quando true, o consumer simula uma falha transitória (para exercitar retry → DLQ).</summary>
    public const string SimularFalha = "simularFalha";

    public const string Plano = "plano";

    /// <summary>Capital em reais inteiros (long): o filtro SQL compara números, e decimal não é bem suportado.</summary>
    public const string CapitalSegurado = "capitalSegurado";

    /// <summary>Definida pela action da regra da assinatura alto-valor (não é enviada pelo publisher).</summary>
    public const string Prioridade = "prioridade";
}

public static class TiposDeMensagem
{
    public const string PropostaRecebida = "PropostaRecebida";
    public const string ApoliceEmitida = "ApoliceEmitida";
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
