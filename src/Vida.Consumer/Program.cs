using Azure.Messaging.ServiceBus;
using Vida.Consumer;
using Vida.Mensageria;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
// Transações entre entidades: o Worker completa na fila e publica no tópico atomicamente.
builder.Services.AddSingleton(sp => ServiceBusClientFactory.Criar(
    ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>()),
    transacoesEntreEntidades: true));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
