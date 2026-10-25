using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Telemetria;
using Microsoft.EntityFrameworkCore;
using Vida.Mensageria.Topologia;
using Vida.Producer.Api;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Erros;
using Vida.Producer.Outbox;

// Producer + ferramentas de operação, como API HTTP em http://localhost:5180.
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
app.MapLogsAoVivo();
app.MapOpenApi();

app.Logger.LogInformation("Namespace do Service Bus: {Namespace}", fqdn);
app.MapGet("/namespace", () => new { Namespace = fqdn });

// Fase 1
app.MapPost("/propostas/lote", async (EnvioDeLote corpo, EnvioDePropostas envio, CancellationToken ct) =>
    corpo.Validar() is { Count: > 0 } erros
        ? Results.ValidationProblem(erros)
        : Results.Ok(new { Propostas = await envio.EnviarLoteAsync(corpo.Quantidade, ct) }));

app.MapPost("/propostas", async (EnvioDeProposta corpo, EnvioDePropostas envio, CancellationToken ct) =>
    corpo.Validar() is { Count: > 0 } erros
        ? Results.ValidationProblem(erros)
        : Results.Ok(await envio.EnviarUmaAsync(corpo, ct)));

app.MapPost("/propostas/invalida", async (EnvioDePropostas envio, CancellationToken ct) =>
    Results.Ok(new { MessageId = await envio.EnviarInvalidaAsync(ct) }));

app.MapGet("/filas/propostas/mensagens", async (bool? dlq, InspecaoDeEntidades inspecao, CancellationToken ct) =>
    await inspecao.EspiarFilaAsync(dlq ?? false, ct));

app.MapPost("/filas/propostas/dlq/reprocessar", async (ReprocessamentoDaDlq reprocessamento, CancellationToken ct) =>
    await reprocessamento.ReprocessarAsync(ct));

// Fase 2
app.MapPost("/apolices", async (PublicacaoDeApolice corpo, PublicacaoDeApolices publicacao, CancellationToken ct) =>
    corpo.Validar() is { Count: > 0 } erros
        ? Results.ValidationProblem(erros)
        : Results.Ok(await publicacao.PublicarAsync(corpo, ct)));

app.MapGet("/assinaturas/regras", async (InspecaoDeEntidades inspecao, CancellationToken ct) =>
    await inspecao.ListarRegrasAsync(ct));

app.MapGet("/assinaturas/{nome}/mensagens", async (string nome, bool? dlq, InspecaoDeEntidades inspecao, CancellationToken ct) =>
    Assinaturas.Todas.Contains(nome)
        ? Results.Ok(await inspecao.EspiarAssinaturaAsync(nome, dlq ?? false, ct))
        : Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Assinatura desconhecida",
            detail: $"'{nome}' não existe. Válidas: {string.Join(", ", Assinaturas.Todas)}."));

// Fase 3
app.MapPost("/endossos/em-ordem", async (SequenciaDeEndossos corpo, EnvioDeEndossos envio, CancellationToken ct) =>
    corpo.Validar() is { Count: > 0 } erros
        ? Results.ValidationProblem(erros)
        : Results.Ok(new { Apolices = await envio.EnviarEmOrdemAsync(corpo.Apolices, corpo.Versoes, ct) }));

app.MapPost("/endossos/fora-de-ordem", async (EnvioDeEndossos envio, CancellationToken ct) =>
{
    var (apoliceId, ordem) = await envio.EnviarForaDeOrdemAsync(versoes: 5, ct);
    return Results.Ok(new { ApoliceId = apoliceId, Ordem = ordem });
});

app.MapPost("/endossos/duplicado", async (EnvioDeEndossos envio, CancellationToken ct) =>
    Results.Ok(new { ApoliceId = await envio.EnviarDuplicadoAsync(ct) }));

// Fase 4
app.MapApiDePropostas();
app.MapOutbox();

app.MapPost("/propostas/sem-outbox", async (EnvioSemOutboxParametros corpo, EnvioSemOutbox envio, BancoDePropostas banco, CancellationToken ct) =>
{
    var (proposta, publicada) = await envio.EnviarAsync(banco, corpo.FalharAntesDoEnvio, ct);
    return Results.Ok(new { proposta.PropostaId, Publicada = publicada });
});

app.Run();
