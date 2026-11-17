using Vida.Plataforma.Api;
using Vida.Plataforma.Conexao;
using Vida.Telemetria;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vida.Propostas.Dados;
using Vida.Propostas.Desfechos;
using Vida.Propostas.Erros;
using Vida.Propostas.Outbox;

// Squad de Propostas: a API de propostas, o outbox e os desfechos, em http://localhost:5180 (controllers em Controllers/).
// Publica o evento PropostaRecebida no tópico proposta-recebida e não sabe quem reage. As ferramentas de operação
// ficam no Vida.Operacao (:5184).
// Em Development, o contrato fica em /openapi/v1.json e a interface para explorar e testar, em http://localhost:5180/scalar.
//
//   POST /api/propostas                     grava proposta + outbox numa transação → 201 Created (Idempotency-Key opcional)
//   GET  /api/propostas/{id}                status: Recebida (no outbox), EnviadaParaAnalise (publicada), Aprovada ou Recusada
//   (sem endpoint) assinaturas status-aprovada e status-recusada: o desfecho da subscrição vira Aprovada/Recusada no GET
//
// Sem a connection string 'Propostas' o serviço não sobe (fail fast): a API, o outbox e os desfechos dependem do banco.

var builder = WebApplication.CreateBuilder(args);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// Fase 4: connection string do SQL Server local, gerada por infra/sql-up.ps1.
var arquivoDoSqlServer = Path.Combine(AppContext.BaseDirectory, "sqlserver.local.json");
builder.Configuration.AddJsonFile(arquivoDoSqlServer, optional: true);
// Arquivos locais são padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais
// (no Azure, a config vem do ambiente; nos testes, o WebApplicationFactory passa a dele como linha de comando).
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Propostas");
builder.Services.AddProblemDetails();
// Vários tratadores: o primeiro que reconhecer a exceção responde (Service Bus e auth, depois banco).
builder.Services.AddExceptionHandler<TratadorDeErrosDoServiceBus>();
builder.Services.AddExceptionHandler<TratadorDeErrosDoBanco>();
builder.Services.AddControllers();
// Documento v1 (/openapi/v1.json), com os endpoints de Controllers/.
builder.Services.AddOpenApi();

// Fail fast: sem banco não há o que servir. Melhor não subir (e o deploy falhar visível) do que subir respondendo 503.
var conexaoSql = builder.Configuration.GetConnectionString("Propostas");
if (string.IsNullOrWhiteSpace(conexaoSql))
{
    throw new InvalidOperationException(
        "Connection string 'Propostas' não encontrada. Localmente: rode infra/sql-up.ps1 e recompile (o arquivo é copiado no build).");
}
// DbContext por requisição (scoped).
builder.Services.AddDbContext<BancoDePropostas>(opcoes => opcoes.UseSqlServer(conexaoSql));
// Ordem importa: hosted services começam na ordem de registro, então o banco é migrado antes do despachante.
builder.Services.AddHostedService<MigracaoDoBanco>();

// despachante do outbox: pega as mensagens pendentes e publica no Service Bus, depois marca como publicadas.
builder.Services.AddHostedService<DespachanteDoOutbox>();

// Desfecho da subscrição de volta para a API (Aprovada/Recusada).
builder.Services.AddHostedService<ProcessadorDeDesfechos>();

var fqdn = ServiceBusClientFactory.ObterNamespace(builder.Configuration);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Criar(fqdn));

var app = builder.Build();
app.UseExceptionHandler();
// Contrato e interface só em Development: em produção a API seria publicada pelo API Management, que importa o
// OpenAPI no deploy. Sem isso, qualquer um enumeraria as rotas, inclusive as de operação.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar lê o documento de /openapi/v1.json.
    app.MapScalarApiReference(opcoes => opcoes.AddDocument("v1", "Vida.Propostas", isDefault: true));
}
// Os endpoints ficam nos controllers (pasta Controllers), descobertos por atributo.
app.MapControllers();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);

app.Run();
