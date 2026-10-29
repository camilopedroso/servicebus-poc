using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Vida.Assinantes.Processamento;
using Vida.LogsAoVivo.Logs;
using Vida.Plataforma.Conexao;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Contratos.Topologia;
using Vida.Telemetria;

// Uso: dotnet run                        → todas as assinaturas ligadas na partida
//      dotnet run -- cobranca alto-valor  → só as informadas começam ligadas
// Depois da partida, o painel (web/painel) liga e desliga cada uma por HTTP (POST /processadores/{nome}/iniciar|parar).
var nomesInformados = args.Where(a => !a.StartsWith('-'));
// Aceita qualquer grafia ("Cobranca", "ALTO-VALOR") e guarda o nome da topologia.
var escolhidas = new List<string>();
var desconhecidas = new List<string>();
foreach (var nome in nomesInformados)
{
    var assinatura = Assinaturas.Interpretar(nome, Assinaturas.DoAssinantes);
    if (assinatura is null)
    {
        desconhecidas.Add(nome);
    }
    else
    {
        escolhidas.Add(assinatura);
    }
}
var assinaturas = escolhidas.Count > 0 ? [.. escolhidas] : Assinaturas.DoAssinantes;
if (desconhecidas.Count > 0)
{
    var listaDeDesconhecidas = string.Join(", ", desconhecidas);
    var listaDeValidas = string.Join(", ", Assinaturas.DoAssinantes);
    Console.Error.WriteLine($"Assinatura(s) desconhecida(s): {listaDeDesconhecidas}. Válidas: {listaDeValidas}.");
    return 1;
}

// Nomes de assinatura não são configuração: só repassa ao host os argumentos no formato --chave=valor.
var opcoesDeLinhaDeComando = args.Where(a => a.StartsWith('-'));
var argumentosDoHost = opcoesDeLinhaDeComando.ToArray();
var builder = WebApplication.CreateBuilder(argumentosDoHost);

var arquivoDoServiceBus = Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal);
builder.Configuration.AddJsonFile(arquivoDoServiceBus, optional: true);
// O arquivo local é padrão de desenvolvimento: variável de ambiente e linha de comando continuam valendo mais.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(argumentosDoHost);
builder.AddLogsAoVivo();
// Fase 5: OpenTelemetry (traces, métricas, logs) para o Application Insights. Depois da config local, que traz a connection string.
builder.AddTelemetria("Vida.Assinantes");
// GET /processadores e POST /processadores/{nome}/iniciar|parar (ProcessadoresController, da Vida.Plataforma).
builder.AddProcessadoresControlaveis();

builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    return ServiceBusClientFactory.Criar(fqdn);
});
// Só para ler contagens e regras (pendentes por assinatura no painel).
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<IConfiguration>();
    var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
    var credencial = new DefaultAzureCredential();
    return new ServiceBusAdministrationClient(fqdn, credencial);
});

// Um processor por assinatura, todos compartilhando o mesmo ServiceBusClient (uma conexão AMQP).
// Todas são registradas; as que não vieram na linha de comando começam desligadas e podem ser ligadas pelo painel.
foreach (var nome in Assinaturas.DoAssinantes)
{
    var ligarNaPartida = assinaturas.Contains(nome);
    builder.Services.AddProcessadorControlavel(sp =>
    {
        var client = sp.GetRequiredService<ServiceBusClient>();
        var logger = sp.GetRequiredService<ILogger<ProcessadorDeAssinatura>>();
        var processadorDeAssinatura = new ProcessadorDeAssinatura(nome, client, logger);
        return processadorDeAssinatura.CriarProcessador(ligarNaPartida);
    });
}

var app = builder.Build();
// Controllers: AssinaturasController (deste app), LogsController (AddLogsAoVivo) e ProcessadoresController (AddProcessadoresControlaveis).
app.MapControllers();
app.Run();
return 0;
