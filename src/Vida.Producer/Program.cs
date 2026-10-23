using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Vida.Mensageria;

// Producer + ferramentas de operação da fila "propostas".
//   dotnet run -- enviar [quantidade]   propostas válidas, enviadas em lote
//   dotnet run -- enviar-invalida       viola regra de negócio → consumer manda para a DLQ
//   dotnet run -- enviar-com-falha      falha transitória simulada → retry até maxDeliveryCount → DLQ
//   dotnet run -- espiar                peek na fila principal (não consome)
//   dotnet run -- dlq                   peek na dead-letter queue, com o motivo
//   dotnet run -- reprocessar-dlq       move mensagens da DLQ de volta para a fila principal

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ServiceBusClientFactory.ArquivoConfiguracaoLocal), optional: true)
    .AddEnvironmentVariables()
    .Build();

var comando = args.FirstOrDefault() ?? "enviar";

await using var client = ServiceBusClientFactory.Criar(ServiceBusClientFactory.ObterNamespace(configuration));

try
{
    switch (comando)
    {
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
catch (Azure.Identity.AuthenticationFailedException ex)
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

static PropostaSeguroVida NovaProposta(int? idade = null) => new(
    PropostaId: Guid.NewGuid(),
    ClienteId: $"CLI-{Random.Shared.Next(1000, 9999)}",
    Idade: idade ?? Random.Shared.Next(18, 70),
    CapitalSegurado: Random.Shared.Next(50, 2000) * 1_000m,
    Plano: Random.Shared.GetItems(["Individual", "Familiar", "Empresarial"], 1)[0],
    CriadaEm: DateTimeOffset.UtcNow);

static ServiceBusMessage CriarMensagem(PropostaSeguroVida proposta, bool simularFalha)
{
    var mensagem = new ServiceBusMessage(BinaryData.FromObjectAsJson(proposta))
    {
        // MessageId = id de negócio: base para detecção de duplicatas (fase 3).
        MessageId = proposta.PropostaId.ToString(),
        ContentType = "application/json",
        // Subject (antigo "Label") descreve o tipo do evento; filtros de assinatura usam isso na fase 2.
        Subject = "PropostaRecebida",
        CorrelationId = proposta.ClienteId,
    };
    mensagem.ApplicationProperties["plano"] = proposta.Plano;
    if (simularFalha)
    {
        mensagem.ApplicationProperties[Propriedades.SimularFalha] = true;
    }
    return mensagem;
}

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

static async Task EnviarUma(ServiceBusClient client, PropostaSeguroVida proposta, bool simularFalha)
{
    await using var sender = client.CreateSender(Filas.Propostas);
    await sender.SendMessageAsync(CriarMensagem(proposta, simularFalha));
    Console.WriteLine($"Enviada proposta {proposta.PropostaId} (idade {proposta.Idade}, simularFalha={simularFalha}).");
}

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
