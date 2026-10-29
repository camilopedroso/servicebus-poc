using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vida.LogsAoVivo.Logs;
using Vida.Plataforma.Api;
using Vida.Plataforma.Conexao;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Analise;
using Vida.Subscricao.Cenarios;
using Vida.Subscricao.Dados;
using Vida.Subscricao.Endossos;
using Vida.Subscricao.Outbox;
using Vida.Telemetria;

// Squad de Subscrição, em http://localhost:5181: analisa as propostas que chegam na caixa de entrada
// (fila subscricao-entrada, alimentada pelo tópico proposta-recebida), emite a apólice ou recusa, e aplica endossos.
// Publica apolice-emitida e proposta-recusada; é a única publicadora desses tópicos. Desde a etapa 9 tem banco
// próprio (VidaSubscricao): a análise grava inbox + apólice/recusa + outbox numa transação, e o despachante publica.
//   GET  /logs                                 logs ao vivo (Vida.LogsAoVivo)
//   GET  /processadores                        liga/desliga (Vida.Plataforma): "propostas" e "endossos"
//   POST /processadores/{nome}/iniciar|parar
// Demonstração (Controllers/Demonstracao, contrato em /openapi/demo.json; interface em /scalar, em Development):
//   POST /demo/apolices                     fase 2: publica uma ApoliceEmitida inventada direto no tópico
//   POST /demo/endossos/em-ordem            fase 3: {apolices, versoes}: v1..vN intercaladas entre apólices (sessões em paralelo)
//   POST /demo/endossos/fora-de-ordem       fase 3: 5 versões de uma apólice embaralhadas → deferral
//   POST /demo/endossos/duplicado           fase 3: v1, v1 de novo (mesmo MessageId) e v2 → o broker descarta a cópia
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// Etapa 9: connection string do SQL Server local (ConnectionStrings:Subscricao), gerada por infra/sql-up.ps1.
var arquivoDoSqlServer = Path.Combine(AppContext.BaseDirectory, "sqlserver.local.json");
builder.Configuration.AddJsonFile(arquivoDoSqlServer, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Subscricao");
// GET /processadores e POST /processadores/{nome}/iniciar|parar: desligar o consumo sem derrubar o processo.
builder.AddProcessadoresControlaveis();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErrosDoServiceBus>();
builder.Services.AddControllers();
// Só as demonstrações têm contrato aqui: a Subscrição não tem API de negócio, ela conversa por eventos.
builder.Services.AddOpenApi("demo", opcoes =>
    opcoes.ShouldInclude = descricao => descricao.GroupName == "demo");

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
// Até a etapa 8 eram dois: um com EnableCrossEntityTransactions (Complete na fila + Send no tópico) e outro sem.
// Com o outbox, a análise não publica mais dentro da transação do broker; a única transação que sobrou (o retry
// com backoff: Complete e Send na própria fila) é numa entidade só, e não precisa da opção. Um client basta.
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn);
});

// Banco da Subscrição. Sem a connection string, a análise não liga: as propostas esperam na fila (em vez de falhar
// 5 vezes e ir para a DLQ), e os endossos e as demonstrações continuam funcionando.
var conexaoSql = builder.Configuration.GetConnectionString("Subscricao");
builder.Services.AddDbContext<BancoDaSubscricao>(opcoes =>
    opcoes.UseSqlServer(conexaoSql ?? throw new InvalidOperationException(
        "Connection string 'Subscricao' não encontrada. Rode infra\\sql-up.ps1 e recompile.")));
// Ordem importa: hosted services começam na ordem de registro, então o banco é migrado antes do despachante.
builder.Services.AddHostedService<MigracaoDoBanco>();
builder.Services.AddScoped<RegistroDaAnalise>();
if (conexaoSql is not null)
{
    builder.Services.AddHostedService<DespachanteDoOutbox>();
}
builder.Services.AddSingleton<ProcessadorDePropostas>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var propostas = sp.GetRequiredService<ProcessadorDePropostas>();
    return propostas.CriarProcessador(ligarNaPartida: conexaoSql is not null);
});

builder.Services.AddSingleton<ProcessadorDeEndossos>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var endossos = sp.GetRequiredService<ProcessadorDeEndossos>();
    return endossos.CriarProcessador();
});
builder.Services.AddSingleton<PublicacaoDeApolices>();
builder.Services.AddSingleton<EnvioDeEndossos>();

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opcoes => opcoes.AddDocument("demo", "Vida.Subscricao (demonstração)", isDefault: true));
}
app.MapControllers();
if (conexaoSql is null)
{
    app.Logger.LogWarning("Sem banco (ConnectionStrings:Subscricao): o processador de propostas fica desligado; rode infra\\sql-up.ps1.");
}
app.Run();
