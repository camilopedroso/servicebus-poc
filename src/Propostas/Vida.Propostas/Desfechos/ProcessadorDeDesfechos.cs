using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Mensagens;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Propostas.Dados;

namespace Vida.Propostas.Desfechos;

/// <summary>
/// Fecha o ciclo da API de propostas: ouve as assinaturas status-aprovada (tópico apolice-emitida) e
/// status-recusada (tópico proposta-recusada) e grava o desfecho na proposta, para o GET mostrar Aprovada ou Recusada.
/// É o mesmo publish/subscribe da fase 2: Propostas é só mais um assinante, com a sua própria cópia dos eventos.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app (sem transação entre entidades, então serve para tudo).</param>
/// <param name="escopos">Um escopo (e um DbContext) por mensagem.</param>
/// <param name="logger">Logger do processador de desfechos.</param>
public sealed class ProcessadorDeDesfechos(
    ServiceBusClient client,
    IServiceScopeFactory escopos,
    ILogger<ProcessadorDeDesfechos> logger) : IHostedService, IAsyncDisposable
{
    /// <summary>Assinatura da squad de Propostas no tópico apolice-emitida (infra/servicos/propostas.assina.bicep).</summary>
    public const string AssinaturaAprovada = "status-aprovada";

    /// <summary>Assinatura da squad de Propostas no tópico proposta-recusada.</summary>
    public const string AssinaturaRecusada = "status-recusada";

    /// <summary>
    /// Motivo de dead-letter de um desfecho cuja proposta não está no banco. Só este serviço o usa, por isso fica
    /// aqui e não no catálogo da plataforma (MotivosDeDeadLetter).
    /// </summary>
    public const string MotivoPropostaDesconhecida = "PropostaDesconhecida";

    private readonly List<ServiceBusProcessor> _processadores = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var aprovadas = CriarProcessador(MensagensApolice.Topico, AssinaturaAprovada, ProcessarAprovacaoAsync);
        var recusadas = CriarProcessador(MensagensRecusa.Topico, AssinaturaRecusada, ProcessarRecusaAsync);
        await aprovadas.StartProcessingAsync(cancellationToken);
        await recusadas.StartProcessingAsync(cancellationToken);
        logger.LogInformation(
            "Desfechos: ouvindo {Aprovadas} e {Recusadas} para atualizar o status das propostas da API.",
            AssinaturaAprovada, AssinaturaRecusada);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processador in _processadores)
        {
            await processador.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processador in _processadores)
        {
            await processador.DisposeAsync();
        }
    }

    private ServiceBusProcessor CriarProcessador(string topico, string assinatura, Func<ProcessMessageEventArgs, Task> tratar)
    {
        var opcoes = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 2,
        };
        var processador = client.CreateProcessor(topico, assinatura, opcoes);
        processador.ProcessMessageAsync += tratar;
        processador.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Desfechos: erro no processor de {Assinatura} ({Origem}).", assinatura, args.ErrorSource);
            return Task.CompletedTask;
        };
        _processadores.Add(processador);
        return processador;
    }

    private async Task ProcessarAprovacaoAsync(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;
        var apolice = Ler<ApoliceEmitida>(mensagem);
        if (apolice is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma ApoliceEmitida válida.");
            return;
        }

        // Exceção do banco (SQL fora do ar) sobe para o processor, que abandona: nova tentativa até maxDeliveryCount.
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var resultado = await RegistroDeDesfechos.AplicarAprovacaoAsync(banco, apolice, args.CancellationToken);
        await ConcluirAsync(args, resultado, apolice.PropostaId, $"aprovada, apólice {apolice.ApoliceId}");
    }

    private async Task ProcessarRecusaAsync(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;
        var recusa = Ler<PropostaRecusada>(mensagem);
        if (recusa is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma PropostaRecusada válida.");
            return;
        }

        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var resultado = await RegistroDeDesfechos.AplicarRecusaAsync(banco, recusa, args.CancellationToken);
        await ConcluirAsync(args, resultado, recusa.PropostaId, $"recusada ({recusa.Motivo})");
    }

    /// <summary>
    /// Conclui a mensagem conforme o resultado. Proposta desconhecida vai para a DLQ: reprocessar não resolve, e
    /// concluir esconderia a inconsistência.
    /// </summary>
    private async Task ConcluirAsync(ProcessMessageEventArgs args, ResultadoDoDesfecho resultado, Guid propostaId, string desfecho)
    {
        var mensagem = args.Message;
        if (resultado == ResultadoDoDesfecho.PropostaDesconhecida)
        {
            await args.DeadLetterMessageAsync(
                mensagem,
                MotivoPropostaDesconhecida,
                $"Proposta {propostaId} não existe no banco de propostas.",
                args.CancellationToken);
        }
        else
        {
            await args.CompleteMessageAsync(mensagem, args.CancellationToken);
        }

        switch (resultado)
        {
            case ResultadoDoDesfecho.Aplicado:
                logger.LogInformation("Desfecho: proposta {PropostaId} {Desfecho}. O GET da API já mostra.", propostaId, desfecho);
                break;
            case ResultadoDoDesfecho.JaDecidida:
                logger.LogInformation("Desfecho: proposta {PropostaId} já tinha desfecho. Evento repetido, nada muda.", propostaId);
                break;
            case ResultadoDoDesfecho.PropostaDesconhecida:
                logger.LogWarning(
                    "Desfecho: proposta {PropostaId} não existe no banco. Mensagem enviada para a DLQ ({Motivo}).",
                    propostaId, MotivoPropostaDesconhecida);
                break;
        }
    }

    /// <summary>
    /// Lê o corpo como o contrato esperado. Corpo ilegível volta null (o chamador manda para a DLQ):
    /// sem o catch, a exceção faria o processor abandonar e gastar as 5 entregas à toa.
    /// </summary>
    private static T? Ler<T>(ServiceBusReceivedMessage mensagem) where T : class
    {
        try
        {
            return mensagem.Body.ToObjectFromJson<T>(SerializacaoDeContratos.Leitura);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
