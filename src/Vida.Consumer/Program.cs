using Vida.Consumer;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;

// Host web só para expor GET /logs ao painel (web/painel). O trabalho continua no Worker.
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);
builder.AddLogsAoVivo();

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
// Transações entre entidades: o Worker completa na fila e publica no tópico atomicamente.
builder.Services.AddSingleton(sp => ServiceBusClientFactory.Criar(
    ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>()),
    transacoesEntreEntidades: true));
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.MapLogsAoVivo();
app.Run();
