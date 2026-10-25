using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;

namespace Vida.Consumer.Endossos;

/// <summary>
/// Fase 3: aplica os endossos de cada apólice em ordem, usando sessions, session state e deferral.
/// Sessions garantem a ordem de CHEGADA por apólice e um consumer por apólice de cada vez.
/// Se o producer mandar fora de ordem (v3 antes da v2), a ordem de chegada não basta:
/// a v3 é adiada (defer) e retomada pelo SequenceNumber quando a v2 for aplicada.
/// </summary>
public sealed class ProcessadorDeEndossos(
    [FromKeyedServices(ProcessadorDeEndossos.ChaveDoClient)] ServiceBusClient client, ILogger<ProcessadorDeEndossos> logger)
    : BackgroundService
{
    /// <summary>Chave do ServiceBusClient próprio (sem transações entre entidades). Ver Program.cs.</summary>
    public const string ChaveDoClient = "endossos";

    private readonly string _instancia = $"{Environment.MachineName}-{Environment.ProcessId}";

    private ServiceBusSessionProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateSessionProcessor(Filas.Endossos, new ServiceBusSessionProcessorOptions
        {
            AutoCompleteMessages = false,
            // Até 8 apólices ao mesmo tempo nesta instância, mas uma mensagem por vez dentro de cada apólice.
            MaxConcurrentSessions = 8,
            MaxConcurrentCallsPerSession = 1,
            // Sem mensagem nova por 3 s, a instância solta a sessão e pode pegar outra (o padrão é mais longo).
            SessionIdleTimeout = TimeSpan.FromSeconds(3),
        });

        _processor.ProcessMessageAsync += ProcessarMensagem;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "[{Instancia}] Erro no processor de endossos ({Origem}).", _instancia, args.ErrorSource);
            return Task.CompletedTask;
        };
        // Sessão aberta/fechada: mostra qual instância "é dona" de cada apólice naquele momento.
        _processor.SessionInitializingAsync += args =>
        {
            logger.LogInformation("[{Instancia}] Sessão da apólice {ApoliceId} aberta nesta instância.", _instancia, args.SessionId);
            return Task.CompletedTask;
        };
        _processor.SessionClosingAsync += args =>
        {
            logger.LogInformation("[{Instancia}] Sessão da apólice {ApoliceId} liberada.", _instancia, args.SessionId);
            return Task.CompletedTask;
        };

        logger.LogInformation("[{Instancia}] Ouvindo a fila '{Fila}' (sessions)...", _instancia, Filas.Endossos);
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

    private async Task ProcessarMensagem(ProcessSessionMessageEventArgs args)
    {
        var mensagem = args.Message;
        var endosso = Ler(mensagem);
        if (endosso is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é um EndossoSolicitado válido.");
            logger.LogError("[{Instancia}] Mensagem {MessageId} enviada para a DLQ: {Motivo}.",
                _instancia, mensagem.MessageId, MotivosDeDeadLetter.PayloadInvalido);
            return;
        }

        var estado = await LerEstado(args);

        // 1) Versão já aplicada: reenvio depois da janela de duplicatas, ou reentrega após cair entre salvar o estado
        //    e completar. Conclui sem aplicar de novo (consumer idempotente).
        if (endosso.Versao <= estado.UltimaVersao)
        {
            await args.CompleteMessageAsync(mensagem, args.CancellationToken);
            logger.LogWarning(
                "[{Instancia}] Apólice {ApoliceId}: endosso v{Versao} já aplicado (última v{Ultima}). Concluído sem reaplicar.",
                _instancia, endosso.ApoliceId, endosso.Versao, estado.UltimaVersao);
            return;
        }

        // 2) Chegou adiantada: adia (defer). A mensagem fica na fila, mas só sai buscada pelo SequenceNumber,
        //    que guardamos no session state. O estado é salvo ANTES do defer: se cair entre os dois, a mensagem
        //    volta e é adiada de novo; na ordem inversa, ficaria adiada sem ninguém saber o número dela.
        if (endosso.Versao > estado.UltimaVersao + 1)
        {
            estado.Adiadas[endosso.Versao] = mensagem.SequenceNumber;
            await SalvarEstado(args, estado);
            await args.DeferMessageAsync(mensagem, cancellationToken: args.CancellationToken);
            logger.LogWarning(
                "[{Instancia}] Apólice {ApoliceId}: endosso v{Versao} chegou antes da v{Esperada}. Adiado (deferred, #{Seq}).",
                _instancia, endosso.ApoliceId, endosso.Versao, estado.UltimaVersao + 1, mensagem.SequenceNumber);
            return;
        }

        // 3) É a próxima versão: aplica e depois retoma, em sequência, as adiadas que ficaram na vez.
        await Aplicar(args, mensagem, endosso, estado);
        while (estado.Adiadas.TryGetValue(estado.UltimaVersao + 1, out var sequencia))
        {
            var adiadas = await args.GetReceiveActions().ReceiveDeferredMessagesAsync([sequencia], args.CancellationToken);
            if (adiadas.Count == 0 || Ler(adiadas[0]) is not { } proximo)
            {
                // Expirou (TTL) ou foi removida: não há como seguir sem ela.
                logger.LogError("[{Instancia}] Apólice {ApoliceId}: endosso adiado #{Seq} não encontrado.",
                    _instancia, endosso.ApoliceId, sequencia);
                break;
            }
            logger.LogInformation("[{Instancia}] Apólice {ApoliceId}: retomando endosso v{Versao} adiado (#{Seq}).",
                _instancia, proximo.ApoliceId, proximo.Versao, sequencia);
            await Aplicar(args, adiadas[0], proximo, estado);
        }
    }

    /// <summary>
    /// Aplica um endosso e registra no session state. O estado é salvo antes do Complete: se cair entre os dois,
    /// a mensagem é reentregue e cai no caso "já aplicado". Session state não entra em transação com o Complete.
    /// </summary>
    private async Task Aplicar(
        ProcessSessionMessageEventArgs args, ServiceBusReceivedMessage mensagem, EndossoSolicitado endosso, EstadoDaSessao estado)
    {
        // Simula a alteração na apólice.
        await Task.Delay(TimeSpan.FromMilliseconds(300), args.CancellationToken);

        estado.UltimaVersao = endosso.Versao;
        estado.Adiadas.Remove(endosso.Versao);
        await SalvarEstado(args, estado);
        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        logger.LogInformation("[{Instancia}] Apólice {ApoliceId}: endosso v{Versao} ({Tipo}) aplicado (#{Seq}).",
            _instancia, endosso.ApoliceId, endosso.Versao, endosso.Tipo, mensagem.SequenceNumber);
    }

    private static async Task<EstadoDaSessao> LerEstado(ProcessSessionMessageEventArgs args)
    {
        var bytes = await args.GetSessionStateAsync(args.CancellationToken);
        // Sessão nova não tem estado (null).
        return bytes is null || bytes.ToMemory().IsEmpty
            ? new EstadoDaSessao()
            : bytes.ToObjectFromJson<EstadoDaSessao>() ?? new EstadoDaSessao();
    }

    private static Task SalvarEstado(ProcessSessionMessageEventArgs args, EstadoDaSessao estado) =>
        args.SetSessionStateAsync(BinaryData.FromObjectAsJson(estado), args.CancellationToken);

    private static EndossoSolicitado? Ler(ServiceBusReceivedMessage mensagem)
    {
        try
        {
            return mensagem.Body.ToObjectFromJson<EndossoSolicitado>(SerializacaoDeContratos.Leitura);
        }
        catch (JsonException)
        {
            return null;
        }
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
