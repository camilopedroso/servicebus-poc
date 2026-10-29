using System.Globalization;
using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Mensagens;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;

namespace Vida.Notificacao.Processamento;

/// <summary>
/// Envia as boas-vindas ao cliente de toda apólice emitida. A assinatura não tem filtro ($Default):
/// recebe também as apólices em revisão, que a Cobrança não recebe.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna da Notificação.</param>
public sealed class ProcessadorDeBoasVindas(ServiceBusClient client, ILogger<ProcessadorDeBoasVindas> logger)
{
    /// <summary>Assinatura da squad no tópico apolice-emitida. É também o nome do processador na rota /processadores.</summary>
    public const string Assinatura = "notificacao";

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
        var processor = client.CreateProcessor(MensagensApolice.Topico, Assinatura, opcoes);

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
        var apolice = LeituraDeContratos.Ler<ApoliceEmitida>(mensagem);
        if (apolice is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma ApoliceEmitida válida.");
            return;
        }

        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        var capital = apolice.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Assinatura}] Apólice {ApoliceId} ({Plano}, capital {Capital}): enviar boas-vindas ao cliente {ClienteId}.",
            Assinatura, apolice.ApoliceId, apolice.Plano, capital, apolice.ClienteId);
    }
}
