using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Vida.LogsAoVivo.Logs;
using Vida.Plataforma.Conexao;
using Vida.Telemetria;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vida.Producer.Cenarios;
using Vida.Producer.Operacao;
using Vida.Producer.Dados;
using Vida.Producer.Desfechos;
using Vida.Producer.Erros;
using Vida.Producer.Outbox;

// Producer + ferramentas de operação, como API HTTP em http://localhost:5180 (controllers em Controllers/).
// O painel (web/painel) chama estes endpoints; também dá para usar curl. Exemplos:
//   curl -X POST localhost:5180/demo/propostas/lote -H "Content-Type: application/json" -d '{"quantidade":5}'
//   curl -X POST localhost:5180/demo/propostas -H "Content-Type: application/json" -d '{"idade":85}'
//   curl localhost:5180/filas/subscricao-entrada/mensagens?dlq=true
//
// Em Development, a interface para explorar e testar fica em http://localhost:5180/scalar (os dois contratos).
//
// Prontos para produção (contrato em /openapi/v1.json):
//   POST /api/propostas                     grava proposta + outbox numa transação → 201 Created (Idempotency-Key opcional)
//   GET  /api/propostas/{id}                status: Recebida (no outbox), EnviadaParaAnalise (publicada), Aprovada ou Recusada
//   GET  /filas/subscricao-entrada/mensagens?dlq=    peek na fila de entrada da Subscrição (ou na DLQ, com o motivo)
//   POST /filas/subscricao-entrada/dlq/reprocessar   devolve a DLQ para a fila (payload inválido fica na DLQ)
//   GET  /assinaturas/{nome}/mensagens?dlq= peek em uma assinatura (ou na DLQ dela)
//   GET  /assinaturas/regras                lista as regras (filtros/actions) de cada assinatura
//   GET  /outbox                            pendentes e publicadas
//   (sem endpoint) assinaturas status-aprovada e status-recusada: o desfecho da subscrição vira Aprovada/Recusada no GET
//
// Demonstração (Controllers/Demonstracao, contrato em /openapi/demo.json): geram dados de teste
// (campos nulos são sorteados) ou simulam falhas, para rodar os cenários do painel.
//   POST /demo/propostas/lote               fase 1: propostas válidas, enviadas em lote
//   POST /demo/propostas                    fase 1: uma proposta: idade > 80 → recusa; simularFalha → retry até maxDeliveryCount → DLQ
//                                           fase 3: com retryComBackoff, novas tentativas agendadas (2 s, 4 s...)
//   POST /demo/propostas/invalida           fase 1: corpo ilegível → DLQ na primeira entrega (PayloadInvalido)
//   POST /demo/apolices                     fase 2: publica uma ApoliceEmitida direto no tópico (sem passar pela Subscrição)
//   POST /demo/endossos/em-ordem            fase 3: {apolices, versoes}: v1..vN intercaladas entre apólices (sessões em paralelo)
//   POST /demo/endossos/fora-de-ordem       fase 3: 5 versões de uma apólice embaralhadas → deferral
//   POST /demo/endossos/duplicado           fase 3: v1, v1 de novo (mesmo MessageId) e v2 → o broker descarta a cópia
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
builder.AddTelemetria("Vida.Producer");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErros>();
builder.Services.AddControllers();
// Esse trecho cria dois documentos:
// - v1: o documento padrão. Por convenção, ele inclui os endpoints sem GroupName e deixa de fora os que têm
//   GroupName = "demo". Aqui ficam as rotas prontas para produção: /api/propostas, /filas, /assinaturas e /outbox.
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
// Administration client = operações de gerenciamento (listar regras) pelo data plane.
builder.Services.AddSingleton(_ =>
{
    var credencial = new DefaultAzureCredential();
    return new ServiceBusAdministrationClient(fqdn, credencial);
});
builder.Services.AddSingleton<EnvioDePropostas>();
builder.Services.AddSingleton<PublicacaoDeApolices>();
builder.Services.AddSingleton<InspecaoDeEntidades>();
builder.Services.AddSingleton<ReprocessamentoDaDlq>();
builder.Services.AddSingleton<EnvioDeEndossos>();
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
        .AddDocument("v1", "Vida.Producer", isDefault: true)
        .AddDocument("demo", "Vida.Producer (demonstração)"));
}
// Os endpoints ficam nos controllers (pasta Controllers, mais o LogsController da Vida.LogsAoVivo), descobertos por atributo.
app.MapControllers();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);

app.Run();
