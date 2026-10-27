using Vida.Consumer;
using Vida.Consumer.Endossos;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Telemetria;

// Host web só para expor GET /logs ao painel (web/painel), pelo LogsController da Vida.LogsAoVivo. O trabalho continua no Worker.
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Consumer");

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
// Transações entre entidades: o Worker completa na fila e publica no tópico atomicamente.
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn, transacoesEntreEntidades: true);
});
builder.Services.AddHostedService<Worker>();

// Fase 3: fila endossos, com sessions. NÃO pode usar o client acima: com transações entre entidades, todos os
// links do client passam pela primeira entidade tocada (a fila propostas, a "via"), e o broker recusa abrir
// um receiver em outra fila ("Local transactions cannot span multiple top-level entities").
// Exceção à regra de um client por app: o client transacional fica exclusivo do fluxo que precisa dele.
builder.Services.AddKeyedSingleton(ProcessadorDeEndossos.ChaveDoClient, (sp, _) =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn);
});
builder.Services.AddHostedService<ProcessadorDeEndossos>();

var app = builder.Build();
app.MapControllers();
app.Run();
