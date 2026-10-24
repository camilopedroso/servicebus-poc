using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes.Processamento;

/// <summary>
/// Processa uma assinatura do tópico "apolice-emitida". Uma instância por assinatura:
/// cada uma recebe sua própria cópia das mensagens que passaram no filtro (publish/subscribe),
/// enquanto várias instâncias da MESMA assinatura dividem as mensagens (competing consumers).
/// Pode ser ligada e desligada em tempo de execução pelo GerenciadorDeAssinaturas.
/// </summary>
/// <param name="assinatura">Nome da assinatura do tópico "apolice-emitida".</param>
/// <param name="client">Instância do ServiceBusClient para criar o processor.</param>
/// <param name="logger">Logger para registrar informações e erros.</param>
public sealed class ProcessadorDeAssinatura(string assinatura, ServiceBusClient client, ILogger<ProcessadorDeAssinatura> logger)
    : IAsyncDisposable
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    private ServiceBusProcessor? _processor;

    /// <summary>Nome da assinatura.</summary>
    public string Nome => assinatura;

    /// <summary>True enquanto o processor estiver buscando mensagens.</summary>
    public bool Ativa => _processor?.IsProcessing == true;

    /// <summary>
    /// Começa a receber mensagens da assinatura. Não faz nada se já estiver ativa.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public async Task IniciarAsync(CancellationToken cancellationToken)
    {
        if (Ativa)
        {
            return;
        }

        // O processor pode ser parado e iniciado de novo: é criado uma vez só e reaproveitado.
        if (_processor is null)
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
        }

        await _processor.StartProcessingAsync(cancellationToken);
        logger.LogInformation("[{Assinatura}] Ouvindo {Topico}/{Assinatura}...", assinatura, Topicos.ApoliceEmitida, assinatura);
    }

    /// <summary>
    /// Para de receber mensagens e espera as que estão em andamento terminarem.
    /// As mensagens novas continuam chegando na assinatura e ficam guardadas até ela ser religada.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public async Task PararAsync(CancellationToken cancellationToken)
    {
        if (!Ativa)
        {
            return;
        }

        await _processor!.StopProcessingAsync(cancellationToken);
        logger.LogInformation(
            "[{Assinatura}] Parada. Novas apólices ficam retidas na assinatura até ela ser religada.", assinatura);
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
    /// Libera o processor ao encerrar o app.
    /// </summary>
    /// <returns></returns>
    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }
}
