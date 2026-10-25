using Vida.Consumer;
using Vida.Consumer.Endossos;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Telemetria;

// Host web só para expor GET /logs ao painel (web/painel). O trabalho continua no Worker.
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Consumer");

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
// Transações entre entidades: o Worker completa na fila e publica no tópico atomicamente.
builder.Services.AddSingleton(sp => ServiceBusClientFactory.Criar(
    ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>()),
    transacoesEntreEntidades: true));
builder.Services.AddHostedService<Worker>();

// Fase 3: fila endossos, com sessions. NÃO pode usar o client acima: com transações entre entidades, todos os
// links do client passam pela primeira entidade tocada (a fila propostas, a "via"), e o broker recusa abrir
// um receiver em outra fila ("Local transactions cannot span multiple top-level entities").
// Exceção à regra de um client por app: o client transacional fica exclusivo do fluxo que precisa dele.
builder.Services.AddKeyedSingleton(ProcessadorDeEndossos.ChaveDoClient, (sp, _) => ServiceBusClientFactory.Criar(
    ServiceBusClientFactory.ObterNamespace(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddHostedService<ProcessadorDeEndossos>();

var app = builder.Build();
app.MapLogsAoVivo();
app.Run();
