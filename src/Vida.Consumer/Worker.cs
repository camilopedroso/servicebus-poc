using Azure.Messaging.ServiceBus;
using Vida.Mensageria;

namespace Vida.Consumer;

/// <summary>
/// Consumer de subscrição: lê propostas da fila e decide o destino de cada mensagem.
/// Rode duas instâncias para ver "competing consumers": cada mensagem vai para só uma delas.
/// </summary>
public sealed class Worker(ServiceBusClient client, IConfiguration configuration, ILogger<Worker> logger)
    : BackgroundService
{
    private const int IdadeMaximaAceita = 80;
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    // Identifica a instância nos logs para comparar quando houver mais de um consumer.
    private readonly string _instancia = $"{Environment.MachineName}-{Environment.ProcessId}";

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(Filas.Propostas, new ServiceBusProcessorOptions
        {
            // PeekLock: a mensagem fica travada (invisível) enquanto processamos; só sai da fila no Complete.
            // ≈ basic.consume com autoAck=false no RabbitMQ. O oposto é ReceiveAndDelete (at-most-once).
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            // Settlement explícito: o código decide Complete / Abandon / DeadLetter.
            AutoCompleteMessages = false,
            // Mensagens processadas em paralelo por esta instância (≈ prefetch/QoS do RabbitMQ).
            MaxConcurrentCalls = configuration.GetValue("Consumer:MaxConcurrentCalls", 4),
            // O SDK renova o lock (lockDuration=30s na fila) enquanto o handler roda, até este limite.
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        });

        _processor.ProcessMessageAsync += ProcessarMensagem;
        _processor.ProcessErrorAsync += TratarErro;

        logger.LogInformation("[{Instancia}] Ouvindo a fila '{Fila}'...", _instancia, Filas.Propostas);
        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal (Ctrl+C).
        }
    }

    private async Task ProcessarMensagem(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;

        PropostaSeguroVida? proposta;
        try
        {
            proposta = mensagem.Body.ToObjectFromJson<PropostaSeguroVida>();
        }
        catch (System.Text.Json.JsonException ex)
        {
            proposta = null;
            logger.LogWarning(ex, "Corpo inválido na mensagem {MessageId}", mensagem.MessageId);
        }

        // 1) Mensagem "venenosa" (não dá para ler): tentar de novo não resolve → DLQ direto.
        if (proposta is null)
        {
            await args.DeadLetterMessageAsync(mensagem, "PayloadInvalido", "Corpo não é uma PropostaSeguroVida válida.");
            return;
        }

        // 2) Falha transitória (ex.: serviço de score fora do ar): Abandon libera o lock e a mensagem
        //    volta para a fila na hora. Após maxDeliveryCount (5) entregas, o broker manda para a DLQ sozinho.
        if (mensagem.ApplicationProperties.TryGetValue(Propriedades.SimularFalha, out var falha) && falha is true)
        {
            logger.LogWarning(
                "[{Instancia}] Falha transitória simulada na proposta {PropostaId} (entrega {Entrega}/5). Abandonando.",
                _instancia, proposta.PropostaId, mensagem.DeliveryCount);
            await args.AbandonMessageAsync(mensagem);
            return;
        }

        // 3) Regra de negócio violada: não é erro técnico, reprocessar dá o mesmo resultado → DLQ com motivo.
        if (proposta.Idade > IdadeMaximaAceita)
        {
            logger.LogWarning(
                "[{Instancia}] Proposta {PropostaId} recusada: idade {Idade} acima de {Maxima}.",
                _instancia, proposta.PropostaId, proposta.Idade, IdadeMaximaAceita);
            await args.DeadLetterMessageAsync(
                mensagem,
                deadLetterReason: "RegraDeNegocio",
                deadLetterErrorDescription: $"Idade {proposta.Idade} acima do limite de {IdadeMaximaAceita} anos.");
            return;
        }

        // 4) Caminho feliz: simula a análise de risco e confirma.
        await Task.Delay(TimeSpan.FromMilliseconds(500), args.CancellationToken);
        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        logger.LogInformation(
            "[{Instancia}] Proposta {PropostaId} aprovada para análise: {Plano}, capital {Capital}, idade {Idade} (#{Seq}).",
            _instancia, proposta.PropostaId, proposta.Plano, proposta.CapitalSegurado.ToString("C0", PtBr), proposta.Idade,
            mensagem.SequenceNumber);
    }

    private Task TratarErro(ProcessErrorEventArgs args)
    {
        // Chamado para erros de infraestrutura (rede, lock perdido, permissão) e exceções não tratadas no handler.
        // O processor segue rodando e tenta de novo; aqui só registramos.
        logger.LogError(args.Exception,
            "[{Instancia}] Erro no processor ({Origem}) em {Entidade}.",
            _instancia, args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            // Para de buscar novas mensagens e espera as em andamento terminarem.
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
