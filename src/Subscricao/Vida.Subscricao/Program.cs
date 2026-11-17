using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vida.Plataforma.Api;
using Vida.Plataforma.Conexao;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Analise;
using Vida.Subscricao.Dados;
using Vida.Subscricao.Endossos;
using Vida.Subscricao.Erros;
using Vida.Subscricao.Outbox;
using Vida.Telemetria;

// Squad de Subscrição, em http://localhost:5181: analisa as propostas que chegam na caixa de entrada
// (fila subscricao-entrada, alimentada pelo tópico proposta-recebida), emite a apólice ou recusa, e aplica endossos.
// Publica apolice-emitida e proposta-recusada; é a única publicadora desses tópicos. Tem banco próprio
// (VidaSubscricao): a análise grava inbox + apólice/recusa + outbox numa transação, e o despachante publica.
// Em Development, o contrato fica em /openapi/v1.json e a interface para explorar e testar, em http://localhost:5181/scalar.
//
//   GET  /api/apolices/{id}                    apólice, versão dos endossos aplicada e endossos pedidos
//   POST /api/apolices/{id}/endossos           grava o endosso (próxima versão) + comando no outbox → 202 (Idempotency-Key opcional)
//   GET  /processadores                        liga/desliga (Vida.Plataforma): "propostas" e "endossos"
//   POST /processadores/{nome}/iniciar|parar
//
// Sem a connection string 'Subscricao' o serviço não sobe (fail fast): análise, endossos e outbox dependem do banco.
var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// Connection string do SQL Server local (ConnectionStrings:Subscricao), gerada por infra/sql-up.ps1.
var arquivoDoSqlServer = Path.Combine(AppContext.BaseDirectory, "sqlserver.local.json");
builder.Configuration.AddJsonFile(arquivoDoSqlServer, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Subscricao");
// GET /processadores e POST /processadores/{nome}/iniciar|parar: desligar o consumo sem derrubar o processo.
builder.AddProcessadoresControlaveis();
builder.Services.AddProblemDetails();
// Vários tratadores: o primeiro que reconhecer a exceção responde (Service Bus e auth, depois banco).
builder.Services.AddExceptionHandler<TratadorDeErrosDoServiceBus>();
builder.Services.AddExceptionHandler<TratadorDeErrosDoBanco>();
builder.Services.AddControllers();
// Documento v1 (/openapi/v1.json), com os endpoints de Controllers/.
builder.Services.AddOpenApi();

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

// Fail fast: sem banco não há o que fazer. Melhor não subir do que consumir mensagens que não dá para gravar.
var conexaoSql = builder.Configuration.GetConnectionString("Subscricao");
if (string.IsNullOrWhiteSpace(conexaoSql))
{
    throw new InvalidOperationException(
        "Connection string 'Subscricao' não encontrada. Localmente: rode infra/sql-up.ps1 e recompile (o arquivo é copiado no build).");
}
builder.Services.AddDbContext<BancoDaSubscricao>(opcoes => opcoes.UseSqlServer(conexaoSql));
// Ordem importa: hosted services começam na ordem de registro, então o banco é migrado antes do despachante.
builder.Services.AddHostedService<MigracaoDoBanco>();
builder.Services.AddScoped<RegistroDaAnalise>();
builder.Services.AddScoped<RegistroDeEndossos>();
builder.Services.AddHostedService<DespachanteDoOutbox>();

builder.Services.AddSingleton<ProcessadorDePropostas>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var propostas = sp.GetRequiredService<ProcessadorDePropostas>();
    return propostas.CriarProcessador();
});

builder.Services.AddSingleton<ProcessadorDeEndossos>();
builder.Services.AddProcessadorControlavel(sp =>
{
    var endossos = sp.GetRequiredService<ProcessadorDeEndossos>();
    return endossos.CriarProcessador();
});

var app = builder.Build();
app.UseExceptionHandler();
// Contrato e interface só em Development: em produção a API seria publicada pelo API Management.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opcoes => opcoes.AddDocument("v1", "Vida.Subscricao", isDefault: true));
}
app.MapControllers();
app.Run();
