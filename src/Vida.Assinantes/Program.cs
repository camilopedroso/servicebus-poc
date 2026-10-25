using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Vida.Assinantes.Endpoints;
using Vida.Assinantes.Processamento;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Telemetria;
using Vida.Mensageria.Topologia;

// Uso: dotnet run                        → todas as assinaturas ligadas na partida
//      dotnet run -- cobranca alto-valor  → só as informadas começam ligadas
// Depois da partida, o painel (web/painel) liga e desliga cada uma por HTTP.
var assinaturas = args.Where(a => !a.StartsWith('-')).ToArray() is { Length: > 0 } escolhidas ? escolhidas : Assinaturas.Todas;
var desconhecidas = assinaturas.Except(Assinaturas.Todas).ToArray();
if (desconhecidas.Length > 0)
{
    Console.Error.WriteLine($"Assinatura(s) desconhecida(s): {string.Join(", ", desconhecidas)}. Válidas: {string.Join(", ", Assinaturas.Todas)}.");
    return 1;
}

// Nomes de assinatura não são configuração: só repassa ao host os argumentos no formato --chave=valor.
var argumentosDoHost = args.Where(a => a.StartsWith('-')).ToArray();
var builder = WebApplication.CreateBuilder(argumentosDoHost);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables().AddCommandLine(argumentosDoHost);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Assinantes");

builder.Services.AddSingleton(sp =>
    ServiceBusClientFactory.Criar(ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>())));
// Só para ler contagens e regras (pendentes por assinatura no painel).
builder.Services.AddSingleton(sp => new ServiceBusAdministrationClient(
    ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>()), new DefaultAzureCredential()));

// Um processor por assinatura, todos compartilhando o mesmo ServiceBusClient (uma conexão AMQP).
builder.Services.AddSingleton(sp => new GerenciadorDeAssinaturas(
    assinaturas,
    sp.GetRequiredService<ServiceBusClient>(),
    sp.GetRequiredService<ILogger<ProcessadorDeAssinatura>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<GerenciadorDeAssinaturas>());

var app = builder.Build();
app.MapLogsAoVivo();
app.MapAssinaturas();
app.Run();
return 0;
