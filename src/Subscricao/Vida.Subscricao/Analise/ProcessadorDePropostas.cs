using System.Transactions;
using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Processamento;
using Vida.Plataforma.Mensagens;
using Vida.Propostas.Contratos.Eventos;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Dominio;
using Vida.Telemetria;

namespace Vida.Subscricao.Analise;

/// <summary>
/// Lê a caixa de entrada da Subscrição (fila subscricao-entrada, alimentada pelo tópico proposta-recebida):
/// lê as propostas, decide o destino de cada mensagem e, quando aprova, grava a apólice e o evento ApoliceEmitida;
/// quando recusa por regra de negócio, a recusa e o evento PropostaRecusada. Grava no banco da Subscrição
/// (RegistroDaAnalise: inbox + negócio + outbox numa transação) e só depois conclui a mensagem; quem publica é o
/// DespachanteDoOutbox. Etapa 9: antes era Complete + Send numa transação do broker, que não inclui o SQL Server.
/// Rode duas instâncias para ver "competing consumers": cada mensagem vai para só uma delas.
/// Quem liga e desliga o processor é o GerenciadorDeProcessadores (POST /processadores/propostas/parar).
/// </summary>
public sealed class ProcessadorDePropostas(
    ServiceBusClient client,
    IServiceScopeFactory escopos,
    IConfiguration configuration,
    ILogger<ProcessadorDePropostas> logger)
    : IAsyncDisposable
{
    /// <summary>Nome do processador na rota /processadores/{nome}.</summary>
    public const string NomeDoProcessador = "propostas";

    /// <summary>
    /// Caixa de entrada da Subscrição, alimentada pela assinatura "subscricao" do tópico proposta-recebida (forwardTo).
    /// É detalhe interno: ninguém de fora envia para cá. É para cá, e não para o tópico, que vão a retentativa
    /// agendada e o reprocessamento da DLQ: no tópico, a cópia chegaria a todas as assinaturas.
    /// </summary>
    public const string Fila = "subscricao-entrada";

    private const int IdadeMaximaAceita = 80;

    // Retry com backoff para falha transitória ao gravar: 2, 4, 8, 16 s; na 5ª tentativa, DLQ.
    private const int MaximoDeTentativas = 5;
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    // Identifica a instância nos logs para comparar quando houver mais de um consumer.
    private readonly string _instancia = $"{Environment.MachineName}-{Environment.ProcessId}";

    // Para reagendar na própria fila (retry com backoff). Na fila, e não no tópico proposta-recebida: lá a cópia
    // chegaria a todas as assinaturas, e um interessado novo receberia cada tentativa.
    private readonly ServiceBusSender _senderEntrada = client.CreateSender(Fila);

    /// <summary>
    /// Cria o processor da fila subscricao-entrada com os handlers desta classe. Chamado uma vez, no registro (Program.cs).
    /// </summary>
    /// <returns>O processor, pronto para o GerenciadorDeProcessadores ligar.</returns>
    public ProcessadorControlavel CriarProcessador()
    {
        var opcoes = new ServiceBusProcessorOptions
        {
            // PeekLock: a mensagem fica travada (invisível) enquanto processamos; só sai da fila no Complete.
            // ≈ basic.consume com autoAck=false no RabbitMQ. O oposto é ReceiveAndDelete (at-most-once).
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            // Settlement explícito: o código decide Complete / Abandon / DeadLetter.
            AutoCompleteMessages = false,
            // Mensagens processadas em paralelo por esta instância (≈ prefetch/QoS do RabbitMQ).
            MaxConcurrentCalls = configuration.GetValue("Subscricao:MaxConcurrentCalls", 4),
            // O SDK renova o lock (lockDuration=30s na fila) enquanto o handler roda, até este limite.
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        };
        var processor = client.CreateProcessor(Fila, opcoes);

        processor.ProcessMessageAsync += ProcessarMensagem;
        processor.ProcessErrorAsync += TratarErro;

        return new ProcessadorControlavel(NomeDoProcessador, processor, logger);
    }

    private async Task ProcessarMensagem(ProcessMessageEventArgs args)
    {
        var mensagem = args.Message;

        PropostaSeguroVida? proposta;
        try
        {
            proposta = mensagem.Body.ToObjectFromJson<PropostaSeguroVida>(SerializacaoDeContratos.Leitura);
        }
        catch (System.Text.Json.JsonException ex)
        {
            proposta = null;
            logger.LogWarning(
                ex,
                "Corpo inválido na mensagem {MessageId}",
                mensagem.MessageId);
        }

        // 1) Mensagem "venenosa" (não dá para ler): tentar de novo não resolve → DLQ direto.
        if (proposta is null)
        {
            await args.DeadLetterMessageAsync(
                mensagem,
                MotivosDeDeadLetter.PayloadInvalido,
                "Corpo não é uma PropostaSeguroVida válida.");

            logger.LogError(
                "[{Instancia}] Mensagem {MessageId} enviada para a DLQ na entrega {Entrega}: {Motivo}. Sem retry, porque repetir dá o mesmo erro.",
                _instancia,
                mensagem.MessageId,
                mensagem.DeliveryCount,
                MotivosDeDeadLetter.PayloadInvalido);

            return;
        }

        // Span de negócio, dentro do span do processor (ServiceBusProcessor.ProcessMessage). As tags viram
        // "custom dimensions" no App Insights: dá para buscar o trace de uma proposta pelo id.
        using var atividade = FonteDeAtividades.Fonte.StartActivity("AnalisarProposta");
        atividade?.SetTag("vida.proposta.id", proposta.PropostaId);
        atividade?.SetTag("vida.proposta.plano", proposta.Plano.ToString());

        // 2) Regra de negócio violada: a proposta FOI analisada, e o resultado é "recusada". Não é falha,
        //    então não vai para a DLQ (lá ninguém avisaria o cliente, e reprocessar daria a mesma recusa).
        //    Grava a recusa e o evento PropostaRecusada no banco, como no caminho feliz, e conclui a mensagem.
        if (proposta.Idade > IdadeMaximaAceita)
        {
            var recusa = new PropostaRecusada(
                PropostaId: proposta.PropostaId,
                ClienteId: proposta.ClienteId,
                Plano: proposta.Plano,
                CapitalSegurado: proposta.CapitalSegurado,
                Idade: proposta.Idade,
                Motivo: $"Idade {proposta.Idade} acima do limite de {IdadeMaximaAceita} anos.",
                RecusadaEm: DateTimeOffset.UtcNow);

            var recusaNova = await TentarRegistrarAsync(
                args, proposta, registro => registro.RegistrarRecusaAsync(mensagem.MessageId, recusa, args.CancellationToken));
            if (recusaNova is null || await ConcluirAsync(args, proposta, recusaNova.Value))
            {
                return;
            }

            atividade?.SetTag("vida.proposta.resultado", "recusada");
            MetricasDeNegocio.PropostasDecididas.Add(
                1,
                new KeyValuePair<string, object?>("resultado", "recusada"));

            logger.LogWarning(
                "[{Instancia}] Proposta {PropostaId} recusada: idade {Idade} acima de {Maxima}. Recusa gravada com o evento no outbox ('{Topico}').",
                _instancia,
                proposta.PropostaId,
                proposta.Idade,
                IdadeMaximaAceita,
                MensagensRecusa.Topico);

            return;
        }

        // 3) Caminho feliz: calcula o prêmio, emite a apólice e grava o evento no outbox.
        var premioMensal = CalculadoraDePremio.CalcularMensal(proposta.CapitalSegurado, proposta.Idade);
        var requerRevisao = PoliticaDeRevisao.RequerRevisao(proposta.CapitalSegurado);
        var apolice = new ApoliceEmitida(
            ApoliceId: Guid.NewGuid(),
            PropostaId: proposta.PropostaId,
            ClienteId: proposta.ClienteId,
            Plano: proposta.Plano,
            CapitalSegurado: proposta.CapitalSegurado,
            PremioMensal: premioMensal,
            EmitidaEm: DateTimeOffset.UtcNow,
            RequerRevisao: requerRevisao);

        // Até a etapa 8: Complete na fila + Send no tópico na mesma transação do broker. Com banco próprio isso não
        // basta: um INSERT no SQL Server não entra na transação do Service Bus. Agora: grava tudo numa transação do
        // SQL (inbox + apólice + outbox) e só então conclui. Se cair entre os dois, a reentrega cai na inbox.
        var apoliceNova = await TentarRegistrarAsync(
            args, proposta, registro => registro.RegistrarAprovacaoAsync(mensagem.MessageId, apolice, args.CancellationToken));
        if (apoliceNova is null || await ConcluirAsync(args, proposta, apoliceNova.Value))
        {
            return;
        }

        atividade?.SetTag("vida.proposta.resultado", "aprovada");
        atividade?.SetTag("vida.apolice.id", apolice.ApoliceId);
        MetricasDeNegocio.PropostasDecididas.Add(
            1,
            new KeyValuePair<string, object?>("resultado", "aprovada"));

        var capital = proposta.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Instancia}] Proposta {PropostaId} aprovada: {Plano}, capital {Capital}, idade {Idade} (#{Seq}) → apólice {ApoliceId} gravada com o evento no outbox.",
            _instancia,
            proposta.PropostaId,
            proposta.Plano,
            capital,
            proposta.Idade,
            mensagem.SequenceNumber,
            apolice.ApoliceId);
    }

    /// <summary>
    /// Grava a análise. Falha transitória (banco fora, timeout, deadlock) vira retentativa agendada com espera
    /// crescente; qualquer outra exceção sobe, o SDK abandona a mensagem e o maxDeliveryCount leva à DLQ.
    /// </summary>
    /// <returns>True se gravou, false se já estava gravada (reentrega), null se a mensagem foi reagendada.</returns>
    private async Task<bool?> TentarRegistrarAsync(
        ProcessMessageEventArgs args, PropostaSeguroVida proposta, Func<RegistroDaAnalise, Task<bool>> registrar)
    {
        try
        {
            return await RegistrarAsync(registrar);
        }
        catch (Exception ex) when (FalhasTransitorias.EhTransitoria(ex))
        {
            await AgendarNovaTentativa(args, proposta, ex);
            return null;
        }
    }

    /// <summary>
    /// Grava a análise num escopo próprio (um DbContext por mensagem; o processor roda mensagens em paralelo).
    /// </summary>
    private async Task<bool> RegistrarAsync(Func<RegistroDaAnalise, Task<bool>> registrar)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var registro = escopo.ServiceProvider.GetRequiredService<RegistroDaAnalise>();
        return await registrar(registro);
    }

    /// <summary>
    /// Conclui a mensagem depois de gravar. Devolve true quando o chamador não deve seguir (nada a registrar no log
    /// do caminho normal): reentrega já processada.
    /// </summary>
    /// <param name="args">Mensagem em processamento.</param>
    /// <param name="proposta">Proposta lida do corpo.</param>
    /// <param name="gravouAgora">False se a inbox já tinha este MessageId (reentrega).</param>
    private async Task<bool> ConcluirAsync(ProcessMessageEventArgs args, PropostaSeguroVida proposta, bool gravouAgora)
    {
        var mensagem = args.Message;
        await args.CompleteMessageAsync(mensagem, args.CancellationToken);
        if (gravouAgora)
        {
            return false;
        }

        // At-least-once: a mesma mensagem voltou depois de já gravada (ex.: o processo caiu entre o commit e o
        // Complete, ou o lock expirou). Conclui sem gravar nem publicar de novo.
        logger.LogWarning(
            "[{Instancia}] Proposta {PropostaId} já processada (MessageId na inbox, entrega {Entrega}): concluída sem gravar nem publicar de novo.",
            _instancia, proposta.PropostaId, mensagem.DeliveryCount);
        return true;
    }

    /// <summary>
    /// Retry com espera: conclui a mensagem atual e agenda uma cópia para daqui a 2, 4, 8... segundos,
    /// na mesma transação (ou as duas coisas acontecem, ou nenhuma). Service Bus não tem backoff nativo
    /// no Abandon; o agendamento (ScheduledEnqueueTime) é a forma de esperar sem segurar o lock.
    /// </summary>
    private async Task AgendarNovaTentativa(ProcessMessageEventArgs args, PropostaSeguroVida proposta, Exception falha)
    {
        var mensagem = args.Message;
        var tentativa = LerTentativa(mensagem);
        if (tentativa >= MaximoDeTentativas)
        {
            await args.DeadLetterMessageAsync(
                mensagem,
                MotivosDeDeadLetter.TentativasEsgotadas,
                $"{tentativa} tentativas com backoff. Última falha: {falha.Message}",
                args.CancellationToken);

            logger.LogError(
                falha,
                "[{Instancia}] Proposta {PropostaId}: {Tentativa} tentativas esgotadas. DLQ.",
                _instancia,
                proposta.PropostaId,
                tentativa);

            return;
        }

        var segundosDeEspera = Math.Pow(2, tentativa);
        var espera = TimeSpan.FromSeconds(segundosDeEspera);
        // Cópia da recebida (corpo e propriedades). É uma mensagem nova: DeliveryCount recomeça,
        // por isso a tentativa vai numa propriedade.
        var copia = new ServiceBusMessage(mensagem)
        {
            ScheduledEnqueueTime = DateTimeOffset.UtcNow + espera,
        };
        copia.ApplicationProperties[PropriedadesDaRetentativa.Tentativa] = tentativa + 1;

        // Transação numa entidade só (Complete e Send na mesma fila): não precisa de EnableCrossEntityTransactions.
        using (var transacao = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await args.CompleteMessageAsync(mensagem, args.CancellationToken);
            // Send com ScheduledEnqueueTime = agendamento. ScheduleMessageAsync faz o mesmo e devolve o
            // SequenceNumber para cancelar depois (CancelScheduledMessageAsync).
            await _senderEntrada.SendMessageAsync(copia, args.CancellationToken);
            transacao.Complete();
        }

        logger.LogWarning(
            "[{Instancia}] Falha transitória na proposta {PropostaId} (tentativa {Tentativa}): {Erro}. Nova tentativa agendada para daqui a {Espera} s.",
            _instancia,
            proposta.PropostaId,
            tentativa,
            falha.Message,
            espera.TotalSeconds);
    }

    private static int LerTentativa(ServiceBusReceivedMessage mensagem)
    {
        var temTentativa = mensagem.ApplicationProperties.TryGetValue(PropriedadesDaRetentativa.Tentativa, out var valor);
        return temTentativa && valor is int tentativa ? tentativa : 1;
    }

    private Task TratarErro(ProcessErrorEventArgs args)
    {
        // Chamado para erros de infraestrutura (rede, lock perdido, permissão) e exceções não tratadas no handler.
        // O processor segue rodando e tenta de novo; aqui só registramos.
        logger.LogError(
            args.Exception,
            "[{Instancia}] Erro no processor ({Origem}) em {Entidade}.",
            _instancia,
            args.ErrorSource,
            args.EntityPath);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Libera os senders no encerramento. O processor já foi parado pelo GerenciadorDeProcessadores.
    /// </summary>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public async ValueTask DisposeAsync()
    {
        await _senderEntrada.DisposeAsync();
    }
}
