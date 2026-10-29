using System.Globalization;
using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Mensagens;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Topologia;

namespace Vida.Notificacao.Processamento;

/// <summary>
/// Avisa o cliente de toda proposta recusada por regra de negócio. A recusa é um desfecho (evento no tópico
/// proposta-recusada), não uma falha: se fosse para a DLQ, ninguém avisaria o cliente.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna da Notificação.</param>
public sealed class ProcessadorDeAvisoDeRecusa(ServiceBusClient client, ILogger<ProcessadorDeAvisoDeRecusa> logger)
{
    /// <summary>Assinatura da squad no tópico proposta-recusada. É também o nome do processador na rota /processadores.</summary>
    public const string Assinatura = "notificacao-recusa";

    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>
    /// Cria o processor da assinatura com os handlers desta classe. Chamado uma vez, no registro (Program.cs).
    /// </summary>
    /// <returns>O processor, pronto para o GerenciadorDeProcessadores ligar.</returns>
    public ProcessadorControlavel CriarProcessador()
    {
        var opcoes = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 2,
        };
        var processor = client.CreateProcessor(Topicos.PropostaRecusada, Assinatura, opcoes);

        processor.ProcessMessageAsync += ProcessarMensagem;
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "[{Assinatura}] Erro no processor ({Origem}).", Assinatura, args.ErrorSource);
            return Task.CompletedTask;
        };

        return new ProcessadorControlavel(Assinatura, processor, logger);
    }

    private async Task ProcessarMensagem(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;
        var recusa = LeituraDeContratos.Ler<PropostaRecusada>(mensagem);
        if (recusa is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma PropostaRecusada válida.");
            return;
        }

        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        var capital = recusa.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Assinatura}] Proposta {PropostaId} ({Plano}, capital {Capital}) recusada: {Motivo} Enviar aviso de recusa ao cliente {ClienteId}.",
            Assinatura, recusa.PropostaId, recusa.Plano, capital, recusa.Motivo, recusa.ClienteId);
    }
}
