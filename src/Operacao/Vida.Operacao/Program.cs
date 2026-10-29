using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Scalar.AspNetCore;
using Vida.LogsAoVivo.Logs;
using Vida.Operacao.Ferramentas;
using Vida.Plataforma.Api;
using Vida.Plataforma.Conexao;
using Vida.Telemetria;

// Squad de Plataforma: ferramentas de operação do namespace, em http://localhost:5184 (controllers em Controllers/).
// Não conhece nenhuma squad: filas, tópicos e assinaturas são descobertos no broker a cada chamada.
//   GET  /filas                                        filas com contagens (ativas, DLQ, agendadas)
//   GET  /filas/{nome}/mensagens?dlq=                  peek na fila (ou na DLQ, com o motivo)
//   POST /filas/{nome}/dlq/reprocessar?removerPropriedade=   devolve a DLQ para a mesma fila
//   GET  /assinaturas                                  assinaturas de todos os tópicos, com contagens e forwardTo
//   GET  /assinaturas/regras                           filtros e actions de cada assinatura
//   GET  /assinaturas/{nome}/mensagens?dlq=            peek numa assinatura (ou na DLQ dela)
//   GET  /namespace                                    namespace em uso (o painel mostra no cabeçalho)
// Em Development, o contrato fica em /openapi/v1.json e a interface em http://localhost:5184/scalar.
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
builder.AddLogsAoVivo();
// OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Operacao");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErrosDoServiceBus>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var fqdn = ServiceBusClientFactory.ObterNamespace(builder.Configuration);
// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Criar(fqdn));
// Administration client = operações de gerenciamento (listar filas, tópicos, assinaturas e regras) pelo data plane.
builder.Services.AddSingleton(_ =>
{
    var credencial = new DefaultAzureCredential();
    return new ServiceBusAdministrationClient(fqdn, credencial);
});
builder.Services.AddSingleton<DescobertaDeEntidades>();
builder.Services.AddSingleton<InspecaoDeEntidades>();
builder.Services.AddSingleton<ReprocessamentoDaDlq>();

var app = builder.Build();
app.UseExceptionHandler();
// Contrato e interface só em Development: sem isso, qualquer um enumeraria as rotas de operação.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opcoes => opcoes.AddDocument("v1", "Vida.Operacao", isDefault: true));
}
app.MapControllers();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);

app.Run();
