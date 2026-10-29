using System.Globalization;
using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Mensagens;
using Vida.Plataforma.Processamento;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Topologia;

namespace Vida.Cobranca.Processamento;

/// <summary>
/// Gera o boleto do 1º prêmio de cada apólice emitida que pode ser cobrada já. Quem decide "pode ser cobrada"
/// é o filtro da assinatura (requerRevisao = FALSE, em infra/servicos/cobranca.assina.bicep), não este código:
/// apólice em revisão manual nem chega aqui. Várias instâncias deste serviço dividem as mensagens (competing
/// consumers); outra squad no mesmo tópico recebe a sua própria cópia (publish/subscribe).
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna da Cobrança.</param>
public sealed class ProcessadorDeCobranca(ServiceBusClient client, ILogger<ProcessadorDeCobranca> logger)
{
    /// <summary>Assinatura da squad no tópico apolice-emitida. É também o nome do processador na rota /processadores.</summary>
    public const string Assinatura = "cobranca";

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
        // Mesma API da fila, só muda o endereço: (tópico, assinatura).
        var processor = client.CreateProcessor(Topicos.ApoliceEmitida, Assinatura, opcoes);

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

        var premio = apolice.PremioMensal.ToString("C", PtBr);
        var capital = apolice.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Assinatura}] Apólice {ApoliceId} ({Plano}, capital {Capital}): gerar boleto do 1º prêmio: {Premio}.",
            Assinatura, apolice.ApoliceId, apolice.Plano, capital, premio);
    }
}
