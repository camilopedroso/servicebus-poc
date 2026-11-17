using Vida.Notificacao.Processamento;
using Vida.Plataforma.Conexao;
using Vida.Plataforma.Processamento;
using Vida.Telemetria;

// Squad de Notificação: assina dois tópicos da Subscrição, apolice-emitida ("notificacao": boas-vindas) e
// proposta-recusada ("notificacao-recusa": aviso de recusa).
// Host web em http://localhost:5183, com as rotas de operação da plataforma:
//   GET  /assinaturas                          filtros e contagens das duas assinaturas (Vida.Plataforma)
//   GET  /processadores                        liga/desliga (Vida.Plataforma)
//   POST /processadores/{notificacao|notificacao-recusa}/iniciar|parar
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Notificacao");
builder.AddProcessadoresControlaveis();

// Um ServiceBusClient por aplicação, compartilhado pelos dois processors (uma conexão AMQP).
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn);
});
builder.Services.AddSingleton<ProcessadorDeBoasVindas>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var boasVindas = sp.GetRequiredService<ProcessadorDeBoasVindas>();
    return boasVindas.CriarProcessador();
});
builder.Services.AddSingleton<ProcessadorDeAvisoDeRecusa>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var avisoDeRecusa = sp.GetRequiredService<ProcessadorDeAvisoDeRecusa>();
    return avisoDeRecusa.CriarProcessador();
});

var app = builder.Build();
app.MapControllers();
app.Run();
