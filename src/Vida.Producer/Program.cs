using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Telemetria;
using Microsoft.EntityFrameworkCore;
using Vida.Producer.Cenarios;
using Vida.Producer.Dados;
using Vida.Producer.Erros;
using Vida.Producer.Outbox;

// Producer + ferramentas de operação, como API HTTP em http://localhost:5180 (controllers em Controllers/).
// O painel (web/painel) chama estes endpoints; também dá para usar curl. Exemplos:
//   curl -X POST localhost:5180/propostas/lote -H "Content-Type: application/json" -d '{"quantidade":5}'
//   curl -X POST localhost:5180/propostas -H "Content-Type: application/json" -d '{"idade":85}'
//   curl localhost:5180/filas/propostas/mensagens?dlq=true
// Fase 1 (fila "propostas"):
//   POST /propostas/lote                    propostas válidas, enviadas em lote
//   POST /propostas                         uma proposta: idade > 80 → recusa publicada em proposta-recusada; simularFalha → retry até maxDeliveryCount → DLQ
//   POST /propostas/invalida                corpo ilegível → falha irrecuperável: DLQ na primeira entrega (PayloadInvalido)
//   GET  /filas/propostas/mensagens?dlq=    peek na fila (ou na DLQ, com o motivo)
//   POST /filas/propostas/dlq/reprocessar   devolve a DLQ para a fila (payload inválido fica na DLQ)
// Fase 2 (tópico "apolice-emitida"):
//   POST /apolices                          publica uma ApoliceEmitida direto no tópico (sem passar pela fila)
//   GET  /assinaturas/{nome}/mensagens?dlq= peek em uma assinatura (ou na DLQ dela)
//   GET  /assinaturas/regras                lista as regras (filtros/actions) de cada assinatura
// Fase 3 (fila "endossos", com sessions e detecção de duplicatas; retry agendado na fila "propostas"):
//   POST /endossos/em-ordem                 {apolices, versoes}: v1..vN intercaladas entre apólices (sessões em paralelo)
//   POST /endossos/fora-de-ordem            5 versões de uma apólice embaralhadas → deferral
//   POST /endossos/duplicado                v1, v1 de novo (mesmo MessageId) e v2 → o broker descarta a cópia
//   POST /propostas com retryComBackoff     falha transitória com novas tentativas agendadas (2 s, 4 s...)
// Fase 4 (API REST + outbox no SQL Server local; contrato em /openapi/v1.json):
//   POST /api/propostas                     grava proposta + outbox numa transação → 201 Created (Idempotency-Key opcional)
//   GET  /api/propostas/{id}                status: Recebida (no outbox) ou EnviadaParaAnalise (publicada)
//   GET  /outbox                            pendentes e publicadas
//   POST /outbox/simulacao                  {indisponivel}: derruba/restabelece o envio do despachante
//   POST /propostas/sem-outbox              {falharAntesDoEnvio}: dual write, o problema que o outbox resolve

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);
// Fase 4: connection string do SQL Server local, gerada por infra/sql-up.ps1.
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "sqlserver.local.json"), optional: true);
// Arquivos locais são padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais
// (no Azure, a config vem do ambiente; nos testes, o WebApplicationFactory passa a dele como linha de comando).
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Producer");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErros>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// DbContext por requisição (scoped). Sem a connection string, as fases 1 a 3 continuam funcionando:
// o erro só aparece quando alguém usa o banco (503 com a instrução, pelo TratadorDeErros).
var conexaoSql = builder.Configuration.GetConnectionString("Propostas");
builder.Services.AddDbContext<BancoDePropostas>(opcoes =>
    opcoes.UseSqlServer(conexaoSql ?? throw new BancoNaoConfiguradoException()));
// Ordem importa: hosted services começam na ordem de registro, então o banco é migrado antes do despachante.
builder.Services.AddHostedService<MigracaoDoBanco>();
builder.Services.AddSingleton<SimulacaoDeIndisponibilidade>();
builder.Services.AddHostedService<DespachanteDoOutbox>();

var fqdn = ServiceBusClientFactory.ObterNamespace(builder.Configuration);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Criar(fqdn));
// Administration client = operações de gerenciamento (listar regras) pelo data plane.
builder.Services.AddSingleton(_ => new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential()));
builder.Services.AddSingleton<EnvioDePropostas>();
builder.Services.AddSingleton<PublicacaoDeApolices>();
builder.Services.AddSingleton<InspecaoDeEntidades>();
builder.Services.AddSingleton<ReprocessamentoDaDlq>();
builder.Services.AddSingleton<EnvioDeEndossos>();
builder.Services.AddSingleton<EnvioSemOutbox>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapOpenApi();
// Os endpoints ficam nos controllers (pasta Controllers, mais o LogsController da Vida.LogsAoVivo), descobertos por atributo.
app.MapControllers();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);

app.Run();
