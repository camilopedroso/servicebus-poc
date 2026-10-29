using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Processamento;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes.Processamento;

/// <summary>
/// Processa uma assinatura (de "apolice-emitida" ou "proposta-recusada"). Uma instância por assinatura:
/// cada uma recebe sua própria cópia das mensagens que passaram no filtro (publish/subscribe),
/// enquanto várias instâncias da MESMA assinatura dividem as mensagens (competing consumers).
/// Pode ser ligada e desligada em tempo de execução (POST /processadores/{assinatura}/parar), pelo GerenciadorDeProcessadores.
/// Desligada, a assinatura continua recebendo cópias do tópico e as guarda até ser religada.
/// </summary>
/// <param name="assinatura">Nome da assinatura do tópico "apolice-emitida".</param>
/// <param name="client">Instância do ServiceBusClient para criar o processor.</param>
/// <param name="logger">Logger para registrar informações e erros.</param>
public sealed class ProcessadorDeAssinatura(string assinatura, ServiceBusClient client, ILogger<ProcessadorDeAssinatura> logger)
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    /// <summary>
    /// Cria o processor da assinatura com os handlers desta classe. Chamado uma vez, no registro (Program.cs).
    /// </summary>
    /// <param name="ligarNaPartida">Se a assinatura começa ligada (argumentos da linha de comando, ou todas).</param>
    /// <returns>O processor, pronto para o GerenciadorDeProcessadores ligar.</returns>
    public ProcessadorControlavel CriarProcessador(bool ligarNaPartida)
    {
        var topico = Assinaturas.TopicoDe(assinatura);
        var opcoes = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 2,
        };
        // Mesma API da fila, só muda o endereço: (tópico, assinatura).
        var processor = client.CreateProcessor(topico, assinatura, opcoes);

        processor.ProcessMessageAsync += ProcessarMensagem;
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "[{Assinatura}] Erro no processor ({Origem}).", assinatura, args.ErrorSource);
            return Task.CompletedTask;
        };

        return new ProcessadorControlavel(assinatura, processor, logger, ligarNaPartida);
    }

    /// <summary>
    /// Processa uma mensagem recebida da assinatura, completando-a ou enviando para dead-letter se inválida.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private Task ProcessarMensagem(ProcessMessageEventArgs args) =>
        // O Subject diz o tipo do evento: cada tópico carrega um tipo, mas o handler não depende disso.
        args.Message.Subject == TiposDeMensagem.PropostaRecusada
            ? ProcessarRecusa(args)
            : ProcessarApolice(args);

    /// <summary>
    /// Processa uma ApoliceEmitida (assinaturas cobranca, notificacao e alto-valor).
    /// </summary>
    private async Task ProcessarApolice(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;
        var apolice = Ler<ApoliceEmitida>(mensagem);
        if (apolice is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma ApoliceEmitida válida.");
            return;
        }

        // "prioridade" não foi enviada pelo publisher: quem colocou foi a action da regra da assinatura alto-valor.
        var prioridade = mensagem.ApplicationProperties.TryGetValue(Propriedades.Prioridade, out var valor)
            ? valor?.ToString()
            : "normal";

        var premio = apolice.PremioMensal.ToString("C", PtBr);
        var acao = assinatura switch
        {
            Assinaturas.Cobranca => $"gerar boleto do 1º prêmio: {premio}",
            Assinaturas.Notificacao => $"enviar boas-vindas ao cliente {apolice.ClienteId}",
            Assinaturas.AltoValor => "encaminhar para resseguro e revisão manual",
            _ => "registrar",
        };

        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        var capital = apolice.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Assinatura}] Apólice {ApoliceId} ({Plano}, capital {Capital}, prioridade {Prioridade}): {Acao}.",
            assinatura, apolice.ApoliceId, apolice.Plano, capital, prioridade, acao);
    }

    /// <summary>
    /// Processa uma PropostaRecusada (assinatura notificacao-recusa): avisa o cliente, de forma definitiva.
    /// </summary>
    private async Task ProcessarRecusa(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;
        var recusa = Ler<PropostaRecusada>(mensagem);
        if (recusa is null)
        {
            await args.DeadLetterMessageAsync(mensagem, MotivosDeDeadLetter.PayloadInvalido, "Corpo não é uma PropostaRecusada válida.");
            return;
        }

        await args.CompleteMessageAsync(mensagem, args.CancellationToken);

        var capital = recusa.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Assinatura}] Proposta {PropostaId} ({Plano}, capital {Capital}) recusada: {Motivo} Enviar aviso de recusa ao cliente {ClienteId}.",
            assinatura, recusa.PropostaId, recusa.Plano, capital, recusa.Motivo, recusa.ClienteId);
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
