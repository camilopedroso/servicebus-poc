using Vida.Cobranca.Processamento;
using Vida.Plataforma.Conexao;
using Vida.Plataforma.Processamento;
using Vida.Telemetria;

// Squad de Cobrança: assina apolice-emitida (assinatura "cobranca") e gera o boleto do 1º prêmio.
// Host web em http://localhost:5182, com as rotas de operação da plataforma:
//   GET  /assinaturas                          filtro e contagens da assinatura (Vida.Plataforma)
//   GET  /processadores                        liga/desliga (Vida.Plataforma)
//   POST /processadores/cobranca/iniciar|parar
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Cobranca");
builder.AddProcessadoresControlaveis();

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn);
});
builder.Services.AddSingleton<ProcessadorDeCobranca>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var cobranca = sp.GetRequiredService<ProcessadorDeCobranca>();
    return cobranca.CriarProcessador();
});

var app = builder.Build();
app.MapControllers();
app.Run();
