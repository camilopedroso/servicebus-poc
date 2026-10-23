using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Vida.Mensageria.Conexao;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Dominio;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;

// Producer + ferramentas de operação.
// Fase 1 (fila "propostas"):
//   dotnet run -- enviar [quantidade]           propostas válidas, enviadas em lote
//   dotnet run -- enviar-invalida               viola regra de negócio → consumer manda para a DLQ
//   dotnet run -- enviar-com-falha              falha transitória simulada → retry até maxDeliveryCount → DLQ
//   dotnet run -- espiar                        peek na fila principal (não consome)
//   dotnet run -- dlq                           peek na dead-letter queue, com o motivo
//   dotnet run -- reprocessar-dlq               move mensagens da DLQ de volta para a fila principal
// Fase 2 (tópico "apolice-emitida"):
//   dotnet run -- publicar-apolice [capital]    publica uma ApoliceEmitida direto no tópico (sem passar pela fila)
//   dotnet run -- espiar-assinatura <nome>      peek em uma assinatura
//   dotnet run -- dlq-assinatura <nome>         peek na DLQ de uma assinatura
//   dotnet run -- regras                        lista as regras (filtros/actions) de cada assinatura

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true)
    .AddEnvironmentVariables()
    .Build();

var comando = args.FirstOrDefault() ?? "enviar";
var fqdn = ServiceBusClientFactory.ObterNamespace(configuration);
Console.WriteLine($"Namespace do Service Bus: {fqdn}");

await using var client = ServiceBusClientFactory.Criar(fqdn);

try
{
    switch (comando)
    {
        case "publicar-apolice":
            await PublicarApolice(client, args.Length > 1 ? decimal.Parse(args[1]) : null);
            break;
        case "espiar-assinatura":
            await EspiarAssinatura(client, ArgumentoAssinatura(args), deadLetter: false);
            break;
        case "dlq-assinatura":
            await EspiarAssinatura(client, ArgumentoAssinatura(args), deadLetter: true);
            break;
        case "regras":
            await ListarRegras(fqdn);
            break;
        case "enviar":
            await EnviarLote(client, args.Length > 1 ? int.Parse(args[1]) : 10);
            break;
        case "enviar-invalida":
            await EnviarUma(client, NovaProposta(idade: 85), simularFalha: false);
            break;
        case "enviar-com-falha":
            await EnviarUma(client, NovaProposta(), simularFalha: true);
            break;
        case "espiar":
            await Espiar(client, Filas.Propostas, deadLetter: false);
            break;
        case "dlq":
            await Espiar(client, Filas.Propostas, deadLetter: true);
            break;
        case "reprocessar-dlq":
            await ReprocessarDlq(client);
            break;
        default:
            Console.WriteLine($"Comando desconhecido: {comando}");
            return 1;
    }
}
catch (ServiceBusException ex)
{
    // Reason classifica o erro (ex.: MessagingEntityNotFound, ServiceBusy); IsTransient diz se vale tentar de novo.
    Console.Error.WriteLine($"Erro do Service Bus ({ex.Reason}, transitório={ex.IsTransient}): {ex.Message}");
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (AuthenticationFailedException ex)
{
    Console.Error.WriteLine($"Falha de autenticação. Rode 'az login' e tente de novo. {ex.Message}");
    return 1;
}
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine($"Sem permissão no namespace (RBAC pode levar alguns minutos para propagar). {ex.Message}");
    return 1;
}

return 0;

/// <summary>
/// Obtém o argumento da assinatura a partir da linha de comando.
/// </summary>
/// <param name="args">Argumentos da linha de comando.</param>
/// <returns>Assinatura obtida a partir dos argumentos.</returns>
static string ArgumentoAssinatura(string[] args) =>
    args.Length > 1 && Assinaturas.Todas.Contains(args[1])
        ? args[1]
        : throw new ArgumentException($"Informe a assinatura: {string.Join(" | ", Assinaturas.Todas)}");

/// Publica uma apólice no tópico ApoliceEmitida.
/// <param name="client">Client do Service Bus.</param>
/// <param name="capital">Capital segurado (opcional; se não informado, será gerado aleatoriamente).</param>
static async Task PublicarApolice(ServiceBusClient client, decimal? capital)
{
    var capitalSegurado = capital ?? Random.Shared.Next(50, 2000) * 1_000m;
    var apolice = new ApoliceEmitida(
        ApoliceId: Guid.NewGuid(),
        PropostaId: Guid.NewGuid(),
        ClienteId: $"CLI-{Random.Shared.Next(1000, 9999)}",
        Plano: Random.Shared.GetItems(["Individual", "Familiar", "Empresarial"], 1)[0],
        CapitalSegurado: capitalSegurado,
        PremioMensal: CalculadoraDePremio.CalcularMensal(capitalSegurado, idade: 40),
        EmitidaEm: DateTimeOffset.UtcNow);

    await using var sender = client.CreateSender(Topicos.ApoliceEmitida);
    await sender.SendMessageAsync(MensagensApolice.Criar(apolice));
    Console.WriteLine($"Publicada apólice {apolice.ApoliceId} com capital {capitalSegurado:N0} em '{Topicos.ApoliceEmitida}'.");
}

/// Espia as mensagens de uma assinatura do tópico ApoliceEmitida.
/// <param name="client">Client do Service Bus.</param>
/// <param name="assinatura">Nome da assinatura.</param>
/// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a fila normal.</param>
static async Task EspiarAssinatura(ServiceBusClient client, string assinatura, bool deadLetter)
{
    await using var receiver = client.CreateReceiver(Topicos.ApoliceEmitida, assinatura, new ServiceBusReceiverOptions
    {
        SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
    });

    var mensagens = await receiver.PeekMessagesAsync(maxMessages: 50);
    var nome = $"{Topicos.ApoliceEmitida}/subscriptions/{assinatura}{(deadLetter ? "/$DeadLetterQueue" : "")}";
    Console.WriteLine($"{mensagens.Count} mensagem(ns) em '{nome}':");

    foreach (var m in mensagens)
    {
        // Mostra as propriedades: é o que os filtros enxergam (a action da regra pode ter adicionado alguma).
        var propriedades = string.Join(", ", m.ApplicationProperties.Select(p => $"{p.Key}={p.Value}"));
        Console.WriteLine($"- #{m.SequenceNumber} {m.Subject} {m.MessageId} [{propriedades}] entregas={m.DeliveryCount}");
        if (deadLetter)
        {
            Console.WriteLine($"    motivo: {m.DeadLetterReason} | {m.DeadLetterErrorDescription}");
        }
    }
}

/// Lista as regras de todas as assinaturas do tópico ApoliceEmitida.
/// <param name="fqdn">FQDN do namespace do Service Bus.</param>
static async Task ListarRegras(string fqdn)
{
    // Administration client = operações de gerenciamento (criar/listar entidades e regras) pelo data plane.
    // A infra de verdade está no Bicep; aqui é só leitura, para conferir o que o broker aplicou.
    var admin = new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential());

    foreach (var assinatura in Assinaturas.Todas)
    {
        Console.WriteLine($"{Topicos.ApoliceEmitida}/{assinatura}:");
        await foreach (var regra in admin.GetRulesAsync(Topicos.ApoliceEmitida, assinatura))
        {
            var filtro = regra.Filter switch
            {
                TrueRuleFilter => "TrueFilter (tudo)",
                SqlRuleFilter sql => $"SQL: {sql.SqlExpression}",
                CorrelationRuleFilter c => $"Correlation: {DescreverCorrelacao(c)}",
                _ => regra.Filter.ToString(),
            };
            var acao = regra.Action is SqlRuleAction a ? $" | action: {a.SqlExpression}" : "";
            Console.WriteLine($"  regra {regra.Name}: {filtro}{acao}");
        }
    }
}

/// Descreve um filtro de correlação.
/// <param name="c">Filtro de correlação.</param>
/// <returns>Descrição legível do filtro.</returns>
static string DescreverCorrelacao(CorrelationRuleFilter c)
{
    var partes = new List<string>();
    if (c.Subject is not null) partes.Add($"Subject = '{c.Subject}'");
    if (c.CorrelationId is not null) partes.Add($"CorrelationId = '{c.CorrelationId}'");
    partes.AddRange(c.ApplicationProperties.Select(p => $"{p.Key} = '{p.Value}'"));
    return string.Join(" AND ", partes);
}

/// Cria uma nova proposta de seguro de vida.
/// <param name="idade">Idade do cliente (opcional; se não informado, será gerada aleatoriamente).</param>
/// <returns>Nova instância de PropostaSeguroVida.</returns>
static PropostaSeguroVida NovaProposta(int? idade = null) => new(
    PropostaId: Guid.NewGuid(),
    ClienteId: $"CLI-{Random.Shared.Next(1000, 9999)}",
    Idade: idade ?? Random.Shared.Next(18, 70),
    CapitalSegurado: Random.Shared.Next(50, 2000) * 1_000m,
    Plano: Random.Shared.GetItems(["Individual", "Familiar", "Empresarial"], 1)[0],
    CriadaEm: DateTimeOffset.UtcNow);

/// Cria uma mensagem do Service Bus a partir de uma proposta.
/// <param name="proposta">Proposta de seguro de vida.</param>
/// <param name="simularFalha">Se true, adiciona a propriedade SimularFalha na mensagem.</param>
/// <returns>Nova instância de ServiceBusMessage.</returns>
static ServiceBusMessage CriarMensagem(PropostaSeguroVida proposta, bool simularFalha)
{
    var mensagem = new ServiceBusMessage(BinaryData.FromObjectAsJson(proposta))
    {
        // MessageId = id de negócio: base para detecção de duplicatas (fase 3).
        MessageId = proposta.PropostaId.ToString(),
        ContentType = "application/json",
        // Subject (antigo "Label") descreve o tipo da mensagem; filtros de assinatura podem usar (fase 2).
        Subject = TiposDeMensagem.PropostaRecebida,
        CorrelationId = proposta.ClienteId,
    };
    mensagem.ApplicationProperties[Propriedades.Plano] = proposta.Plano;
    if (simularFalha)
    {
        mensagem.ApplicationProperties[Propriedades.SimularFalha] = true;
    }
    return mensagem;
}

/// Envia um lote de propostas para a fila.
/// <param name="client">Client do Service Bus.</param>
/// <param name="quantidade">Número de propostas a enviar.</param>
/// <returns>Tarefa representando a operação assíncrona.</returns>
static async Task EnviarLote(ServiceBusClient client, int quantidade)
{
    await using var sender = client.CreateSender(Filas.Propostas);

    // Batch: várias mensagens numa única operação de rede. O SDK respeita o limite de tamanho
    // do tier (256 KB no Standard); TryAddMessage retorna false quando o lote enche.
    var enviadas = 0;
    while (enviadas < quantidade)
    {
        using var lote = await sender.CreateMessageBatchAsync();
        while (enviadas < quantidade && lote.TryAddMessage(CriarMensagem(NovaProposta(), simularFalha: false)))
        {
            enviadas++;
        }
        await sender.SendMessagesAsync(lote);
        Console.WriteLine($"Lote enviado com {lote.Count} mensagem(ns).");
    }
    Console.WriteLine($"Total: {enviadas} proposta(s) em '{Filas.Propostas}'.");
}

/// Envia uma única proposta para a fila.
/// <param name="client">Client do Service Bus.</param>
/// <param name="proposta">Proposta de seguro de vida.</param>
/// <param name="simularFalha">Se true, adiciona a propriedade SimularFalha na mensagem.</param>
/// <returns>Tarefa representando a operação assíncrona.</returns>
static async Task EnviarUma(ServiceBusClient client, PropostaSeguroVida proposta, bool simularFalha)
{
    await using var sender = client.CreateSender(Filas.Propostas);
    await sender.SendMessageAsync(CriarMensagem(proposta, simularFalha));
    Console.WriteLine($"Enviada proposta {proposta.PropostaId} (idade {proposta.Idade}, simularFalha={simularFalha}).");
}

/// Espia mensagens de uma fila ou da DLQ.
/// <param name="client">Client do Service Bus.</param>
/// <param name="fila">Nome da fila.</param>
/// <param name="deadLetter">Se true, espiar a DLQ; caso contrário, a fila normal.</param>
/// <returns>Tarefa representando a operação assíncrona.</returns>
static async Task Espiar(ServiceBusClient client, string fila, bool deadLetter)
{
    // Peek não trava nem consome: útil para inspeção/suporte, sem afetar consumers.
    await using var receiver = client.CreateReceiver(fila, new ServiceBusReceiverOptions
    {
        SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
    });

    var mensagens = await receiver.PeekMessagesAsync(maxMessages: 50);
    var nome = deadLetter ? $"{fila}/$DeadLetterQueue" : fila;
    Console.WriteLine($"{mensagens.Count} mensagem(ns) em '{nome}':");

    foreach (var m in mensagens)
    {
        var proposta = m.Body.ToObjectFromJson<PropostaSeguroVida>();
        Console.WriteLine($"- #{m.SequenceNumber} {m.MessageId} idade={proposta?.Idade} entregas={m.DeliveryCount}");
        if (deadLetter)
        {
            Console.WriteLine($"    motivo: {m.DeadLetterReason} | {m.DeadLetterErrorDescription}");
        }
    }
}

/// Reprocessa mensagens da DLQ de volta para a fila principal.
/// <param name="client">Client do Service Bus.</param>
/// <returns>Tarefa representando a operação assíncrona.</returns>
static async Task ReprocessarDlq(ServiceBusClient client)
{
    await using var dlqReceiver = client.CreateReceiver(Filas.Propostas, new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter,
    });
    await using var sender = client.CreateSender(Filas.Propostas);

    var total = 0;
    while (true)
    {
        var mensagens = await dlqReceiver.ReceiveMessagesAsync(maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(3));
        if (mensagens.Count == 0) break;

        foreach (var m in mensagens)
        {
            // Copia corpo e propriedades para uma nova mensagem (DeliveryCount volta a 0).
            // Remove o gatilho de falha simulada para a mensagem conseguir ser processada.
            var reenvio = new ServiceBusMessage(m);
            reenvio.ApplicationProperties.Remove(Propriedades.SimularFalha);
            await sender.SendMessageAsync(reenvio);
            // Só completa na DLQ depois do reenvio: se cair no meio, no pior caso duplica (at-least-once), não perde.
            await dlqReceiver.CompleteMessageAsync(m);
            total++;
        }
    }
    Console.WriteLine($"{total} mensagem(ns) movida(s) da DLQ de volta para '{Filas.Propostas}'.");
}
