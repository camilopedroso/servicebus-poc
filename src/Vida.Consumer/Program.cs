using Azure.Messaging.ServiceBus;
using Vida.Consumer;
using Vida.Mensageria;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(sp =>
    ServiceBusClientFactory.Criar(ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
