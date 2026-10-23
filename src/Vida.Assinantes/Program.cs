using Azure.Messaging.ServiceBus;
using Vida.Assinantes;
using Vida.Mensageria;

// Uso: dotnet run                        → todas as assinaturas
//      dotnet run -- cobranca alto-valor  → só as informadas
var assinaturas = args.Where(a => !a.StartsWith('-')).ToArray() is { Length: > 0 } escolhidas ? escolhidas : Assinaturas.Todas;
var desconhecidas = assinaturas.Except(Assinaturas.Todas).ToArray();
if (desconhecidas.Length > 0)
{
    Console.Error.WriteLine($"Assinatura(s) desconhecida(s): {string.Join(", ", desconhecidas)}. Válidas: {string.Join(", ", Assinaturas.Todas)}.");
    return 1;
}

// Nomes de assinatura não são configuração: só repassa ao host os argumentos no formato --chave=valor.
var builder = Host.CreateApplicationBuilder(args.Where(a => a.StartsWith('-')).ToArray());

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);

builder.Services.AddSingleton(sp =>
    ServiceBusClientFactory.Criar(ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>())));

// Um hosted service por assinatura, todos compartilhando o mesmo ServiceBusClient (uma conexão AMQP).
foreach (var assinatura in assinaturas)
{
    builder.Services.AddSingleton<IHostedService>(sp => new AssinaturaWorker(
        assinatura,
        sp.GetRequiredService<ServiceBusClient>(),
        sp.GetRequiredService<ILogger<AssinaturaWorker>>()));
}

var host = builder.Build();
host.Run();
return 0;
