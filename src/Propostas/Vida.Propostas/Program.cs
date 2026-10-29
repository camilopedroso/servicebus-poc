using Vida.LogsAoVivo.Logs;
using Vida.Plataforma.Api;
using Vida.Plataforma.Conexao;
using Vida.Telemetria;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vida.Propostas.Cenarios;
using Vida.Propostas.Dados;
using Vida.Propostas.Desfechos;
using Vida.Propostas.Erros;
using Vida.Propostas.Outbox;

// Squad de Propostas: a API de propostas, o outbox e os desfechos, em http://localhost:5180 (controllers em Controllers/).
// Publica o evento PropostaRecebida no tópico proposta-recebida e não sabe quem reage. As ferramentas de operação
// ficam no Vida.Operacao (:5184); as demos de apólice e endosso, na Subscrição (:5181), dona desses tópicos e filas.
// O painel (web/painel) chama estes endpoints; também dá para usar curl. Exemplos:
//   curl -X POST localhost:5180/demo/propostas/lote -H "Content-Type: application/json" -d '{"quantidade":5}'
//   curl -X POST localhost:5180/demo/propostas -H "Content-Type: application/json" -d '{"idade":85}'
//
// Em Development, a interface para explorar e testar fica em http://localhost:5180/scalar (os dois contratos).
//
// Prontos para produção (contrato em /openapi/v1.json):
//   POST /api/propostas                     grava proposta + outbox numa transação → 201 Created (Idempotency-Key opcional)
//   GET  /api/propostas/{id}                status: Recebida (no outbox), EnviadaParaAnalise (publicada), Aprovada ou Recusada
//   (sem endpoint) assinaturas status-aprovada e status-recusada: o desfecho da subscrição vira Aprovada/Recusada no GET
//
// Demonstração (Controllers/Demonstracao, contrato em /openapi/demo.json): geram dados de teste
// (campos nulos são sorteados) ou simulam falhas, para rodar os cenários do painel.
//   POST /demo/propostas/lote               fase 1: propostas válidas, enviadas em lote
//   POST /demo/propostas                    fase 1: uma proposta: idade > 80 → recusa; simularFalha → retry até maxDeliveryCount → DLQ
//                                           fase 3: com retryComBackoff, novas tentativas agendadas (2 s, 4 s...)
//   POST /demo/propostas/invalida           fase 1: corpo ilegível → DLQ na primeira entrega (PayloadInvalido)
//   GET  /demo/outbox                       fase 4: pendentes e publicadas no outbox
//   POST /demo/outbox/simulacao             fase 4: {indisponivel}: derruba/restabelece o envio do despachante
//   POST /demo/propostas/sem-outbox         fase 4: {falharAntesDoEnvio}: dual write, o problema que o outbox resolve

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
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Propostas");
builder.Services.AddProblemDetails();
// Vários tratadores: o primeiro que reconhecer a exceção responde (Service Bus e auth, depois banco).
builder.Services.AddExceptionHandler<TratadorDeErrosDoServiceBus>();
builder.Services.AddExceptionHandler<TratadorDeErrosDoBanco>();
builder.Services.AddControllers();
// Esse trecho cria dois documentos:
// - v1: o documento padrão. Por convenção, ele inclui os endpoints sem GroupName e deixa de fora os que têm
//   GroupName = "demo". Aqui fica só a rota pronta para produção: /api/propostas.
// - demo: o ShouldInclude filtra só os endpoints do grupo "demo".
builder.Services.AddOpenApi();
builder.Services.AddOpenApi("demo", opcoes =>
    opcoes.ShouldInclude = descricao => descricao.GroupName == "demo");

// DbContext por requisição (scoped). Sem a connection string, as fases 1 a 3 continuam funcionando:
// o erro só aparece quando alguém usa o banco (503 com a instrução, pelo TratadorDeErros).
var conexaoSql = builder.Configuration.GetConnectionString("Propostas");
builder.Services.AddDbContext<BancoDePropostas>(opcoes =>
    opcoes.UseSqlServer(conexaoSql ?? throw new BancoNaoConfiguradoException()));
// Ordem importa: hosted services começam na ordem de registro, então o banco é migrado antes do despachante.
builder.Services.AddHostedService<MigracaoDoBanco>();
builder.Services.AddSingleton<SimulacaoDeIndisponibilidade>();
builder.Services.AddHostedService<DespachanteDoOutbox>();
// Desfecho da subscrição de volta para a API (Aprovada/Recusada). Sem SQL não há onde gravar: sem o processor,
// os eventos ficam guardados nas assinaturas (TTL de 14 dias) em vez de falhar 5 vezes e ir para a DLQ.
if (conexaoSql is not null)
{
    builder.Services.AddHostedService<ProcessadorDeDesfechos>();
}

var fqdn = ServiceBusClientFactory.ObterNamespace(builder.Configuration);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Criar(fqdn));
builder.Services.AddSingleton<EnvioDePropostas>();
builder.Services.AddSingleton<EnvioSemOutbox>();

var app = builder.Build();
app.UseExceptionHandler();
// Contrato e interface só em Development: em produção a API seria publicada pelo API Management, que importa o
// OpenAPI no deploy. Sem isso, qualquer um enumeraria as rotas, inclusive as de operação.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar lê os documentos de /openapi/{nome}.json; "v1" abre primeiro.
    app.MapScalarApiReference(opcoes => opcoes
        .AddDocument("v1", "Vida.Propostas", isDefault: true)
        .AddDocument("demo", "Vida.Propostas (demonstração)"));
}
// Os endpoints ficam nos controllers (pasta Controllers, mais o LogsController da Vida.LogsAoVivo), descobertos por atributo.
app.MapControllers();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);

app.Run();
