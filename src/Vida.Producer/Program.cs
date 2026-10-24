using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Vida.LogsAoVivo.Logs;
using Vida.Mensageria.Conexao;
using Vida.Mensageria.Topologia;
using Vida.Producer.Cenarios;
using Vida.Producer.Contratos;
using Vida.Producer.Erros;

// Producer + ferramentas de operação, como API HTTP em http://localhost:5180.
// O painel (web/painel) chama estes endpoints; também dá para usar curl. Exemplos:
//   curl -X POST localhost:5180/propostas/lote -H "Content-Type: application/json" -d '{"quantidade":5}'
//   curl -X POST localhost:5180/propostas -H "Content-Type: application/json" -d '{"idade":85}'
//   curl localhost:5180/filas/propostas/mensagens?dlq=true
// Fase 1 (fila "propostas"):
//   POST /propostas/lote                    propostas válidas, enviadas em lote
//   POST /propostas                         uma proposta: idade > 80 → recusa publicada em proposta-recusada; simularFalha → retry até maxDeliveryCount → DLQ
//   GET  /filas/propostas/mensagens?dlq=    peek na fila (ou na DLQ, com o motivo)
//   POST /filas/propostas/dlq/reprocessar   devolve a DLQ para a fila (payload inválido fica na DLQ)
// Fase 2 (tópico "apolice-emitida"):
//   POST /apolices                          publica uma ApoliceEmitida direto no tópico (sem passar pela fila)
//   GET  /assinaturas/{nome}/mensagens?dlq= peek em uma assinatura (ou na DLQ dela)
//   GET  /assinaturas/regras                lista as regras (filtros/actions) de cada assinatura

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true);
builder.AddLogsAoVivo();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErros>();

var fqdn = ServiceBusClientFactory.ObterNamespace(builder.Configuration);

// Um ServiceBusClient por aplicação (singleton): ele mantém a conexão AMQP e é thread-safe.
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Criar(fqdn));
// Administration client = operações de gerenciamento (listar regras) pelo data plane.
builder.Services.AddSingleton(_ => new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential()));
builder.Services.AddSingleton<EnvioDePropostas>();
builder.Services.AddSingleton<PublicacaoDeApolices>();
builder.Services.AddSingleton<InspecaoDeEntidades>();
builder.Services.AddSingleton<ReprocessamentoDaDlq>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapLogsAoVivo();

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

app.Run();
