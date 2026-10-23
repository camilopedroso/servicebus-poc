using Azure.Messaging.ServiceBus;
using Vida.Mensageria;

namespace Vida.Assinantes;

/// <summary>
/// Processa uma assinatura do tópico "apolice-emitida". Uma instância por assinatura:
/// cada uma recebe sua própria cópia das mensagens que passaram no filtro (pub/sub),
/// enquanto várias instâncias da MESMA assinatura dividem as mensagens (competing consumers).
/// </summary>
public sealed class AssinaturaWorker(string assinatura, ServiceBusClient client, ILogger<AssinaturaWorker> logger)
    : BackgroundService
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Mesma API da fila, só muda o endereço: (tópico, assinatura).
        _processor = client.CreateProcessor(Topicos.ApoliceEmitida, assinatura, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 2,
        });

        _processor.ProcessMessageAsync += ProcessarMensagem;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "[{Assinatura}] Erro no processor ({Origem}).", assinatura, args.ErrorSource);
            return Task.CompletedTask;
        };

        logger.LogInformation("[{Assinatura}] Ouvindo {Topico}/{Assinatura}...", assinatura, Topicos.ApoliceEmitida, assinatura);
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
        var apolice = mensagem.Body.ToObjectFromJson<ApoliceEmitida>();
        if (apolice is null)
        {
            await args.DeadLetterMessageAsync(mensagem, "PayloadInvalido", "Corpo não é uma ApoliceEmitida válida.");
            return;
        }

        // "prioridade" não foi enviada pelo publisher: quem colocou foi a action da regra da assinatura alto-valor.
        var prioridade = mensagem.ApplicationProperties.TryGetValue(Propriedades.Prioridade, out var valor)
            ? valor?.ToString()
            : "normal";

        var acao = assinatura switch
        {
            Assinaturas.Cobranca => $"gerar boleto do 1º prêmio: {apolice.PremioMensal.ToString("C", PtBr)}",
            Assinaturas.Notificacao => $"enviar boas-vindas ao cliente {apolice.ClienteId}",
            Assinaturas.AltoValor => "encaminhar para resseguro e revisão manual",
            _ => "registrar",
        };

        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        logger.LogInformation(
            "[{Assinatura}] Apólice {ApoliceId} ({Plano}, capital {Capital}, prioridade {Prioridade}): {Acao}.",
            assinatura, apolice.ApoliceId, apolice.Plano, apolice.CapitalSegurado.ToString("C0", PtBr), prioridade, acao);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
