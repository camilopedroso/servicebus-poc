using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes;

/// <summary>
/// Processa uma assinatura do tópico "apolice-emitida". Uma instância por assinatura:
/// cada uma recebe sua própria cópia das mensagens que passaram no filtro (publish/subscribe),
/// enquanto várias instâncias da MESMA assinatura dividem as mensagens (competing consumers).
/// </summary>
/// <param name="assinatura">Nome da assinatura do tópico "apolice-emitida".</param>
/// <param name="client">Instância do ServiceBusClient para criar o processor.</param>
/// <param name="logger">Logger para registrar informações e erros.</param>
public sealed class AssinaturaWorker(string assinatura, ServiceBusClient client, ILogger<AssinaturaWorker> logger)
    : BackgroundService
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    private ServiceBusProcessor? _processor;

    /// <summary>
    /// Executa o processamento das mensagens da assinatura até que o token de cancelamento seja acionado.
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Processa uma mensagem recebida da assinatura, completando-a ou enviando para dead-letter se inválida.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Para encerrar o processamento da assinatura, parando o processor e liberando recursos.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
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
