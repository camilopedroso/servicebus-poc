using System.Transactions;
using Azure.Messaging.ServiceBus;
using Vida.Plataforma.Processamento;
using Vida.Plataforma.Mensagens;
using Vida.Propostas.Contratos.Eventos;
using Vida.Propostas.Contratos.Mensagens;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Contratos.Topologia;
using Vida.Subscricao.Contratos.Dominio;
using Vida.Telemetria;

namespace Vida.Consumer;

/// <summary>
/// Consumer da caixa de entrada da Subscrição (fila subscricao-entrada, alimentada pelo tópico proposta-recebida):
/// lê as propostas, decide o destino de cada mensagem e,
/// quando aprova, publica ApoliceEmitida; quando recusa por regra de negócio, publica PropostaRecusada.
/// Rode duas instâncias para ver "competing consumers": cada mensagem vai para só uma delas.
/// Quem liga e desliga o processor é o GerenciadorDeProcessadores (POST /processadores/propostas/parar).
/// </summary>
public sealed class Worker(ServiceBusClient client, IConfiguration configuration, ILogger<Worker> logger)
    : IAsyncDisposable
{
    /// <summary>Nome do processador na rota /processadores/{nome} e no painel.</summary>
    public const string NomeDoProcessador = "propostas";

    private const int IdadeMaximaAceita = 80;

    // Retry com backoff: a falha simulada some nesta tentativa; acima do máximo, DLQ.
    private const int TentativaEmQueADependenciaVolta = 3;
    private const int MaximoDeTentativas = 5;
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    // Identifica a instância nos logs para comparar quando houver mais de um consumer.
    private readonly string _instancia = $"{Environment.MachineName}-{Environment.ProcessId}";

    // Sender criado a partir do mesmo client do processor: exigência da transação entre entidades.
    private readonly ServiceBusSender _senderApolices = client.CreateSender(Topicos.ApoliceEmitida);
    private readonly ServiceBusSender _senderRecusas = client.CreateSender(Topicos.PropostaRecusada);
    // Para reagendar na própria fila (retry com backoff). Na fila, e não no tópico proposta-recebida: lá a cópia
    // chegaria a todas as assinaturas, e um interessado novo receberia cada tentativa.
    private readonly ServiceBusSender _senderEntrada = client.CreateSender(Filas.SubscricaoEntrada);

    /// <summary>
    /// Cria o processor da fila subscricao-entrada com os handlers deste Worker. Chamado uma vez, no registro (Program.cs).
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
            MaxConcurrentCalls = configuration.GetValue("Consumer:MaxConcurrentCalls", 4),
            // O SDK renova o lock (lockDuration=30s na fila) enquanto o handler roda, até este limite.
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        };
        var processor = client.CreateProcessor(Filas.SubscricaoEntrada, opcoes);

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

        var simularFalha = mensagem.ApplicationProperties.TryGetValue(PropriedadesDaProposta.SimularFalha, out var falha) && falha is true;
        var comBackoff = mensagem.ApplicationProperties.TryGetValue(PropriedadesDaProposta.RetryComBackoff, out var backoff) && backoff is true;
        var tentativa = mensagem.ApplicationProperties.TryGetValue(PropriedadesDaProposta.Tentativa, out var t) && t is int n ? n : 1;

        // 2a) Falha transitória com retry agendado (fase 3). A dependência simulada volta na 3ª tentativa:
        //     com backoff, ela teve tempo de se recuperar; com Abandon (2b), as 5 entregas acabam em segundos.
        if (simularFalha && comBackoff && tentativa < TentativaEmQueADependenciaVolta)
        {
            await AgendarNovaTentativa(args, proposta, tentativa);
            return;
        }
        if (simularFalha && comBackoff)
        {
            logger.LogInformation(
                "[{Instancia}] Dependência de volta: proposta {PropostaId} processada na tentativa {Tentativa}.",
                _instancia,
                proposta.PropostaId,
                tentativa);
        }

        // 2b) Falha transitória (ex.: serviço de score fora do ar): Abandon libera o lock e a mensagem
        //    volta para a fila na hora. Após maxDeliveryCount (5) entregas, o broker manda para a DLQ sozinho.
        if (simularFalha && !comBackoff)
        {
            logger.LogWarning(
                "[{Instancia}] Falha transitória simulada na proposta {PropostaId} (entrega {Entrega}). Abandonando; após maxDeliveryCount entregas, o broker manda para a DLQ.",
                _instancia,
                proposta.PropostaId,
                mensagem.DeliveryCount);

            await args.AbandonMessageAsync(mensagem);

            return;
        }

        // Span de negócio, dentro do span do processor (ServiceBusProcessor.ProcessMessage). As tags viram
        // "custom dimensions" no App Insights: dá para buscar o trace de uma proposta pelo id.
        using var atividade = FonteDeAtividades.Fonte.StartActivity("AnalisarProposta");
        atividade?.SetTag("vida.proposta.id", proposta.PropostaId);
        atividade?.SetTag("vida.proposta.plano", proposta.Plano.ToString());

        // 3) Regra de negócio violada: a proposta FOI analisada, e o resultado é "recusada". Não é falha,
        //    então não vai para a DLQ (lá ninguém avisaria o cliente, e reprocessar daria a mesma recusa).
        //    Conclui na fila e publica PropostaRecusada, na mesma transação do caminho feliz.
        if (proposta.Idade > IdadeMaximaAceita)
        {
            atividade?.SetTag("vida.proposta.resultado", "recusada");
            MetricasDeNegocio.PropostasDecididas.Add(
                1,
                new KeyValuePair<string, object?>("resultado", "recusada"));

            var recusa = new PropostaRecusada(
                PropostaId: proposta.PropostaId,
                ClienteId: proposta.ClienteId,
                Plano: proposta.Plano,
                CapitalSegurado: proposta.CapitalSegurado,
                Idade: proposta.Idade,
                Motivo: $"Idade {proposta.Idade} acima do limite de {IdadeMaximaAceita} anos.",
                RecusadaEm: DateTimeOffset.UtcNow);

            using (var transacao = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                await args.CompleteMessageAsync(mensagem, args.CancellationToken);
                var mensagemDeRecusa = MensagensRecusa.Criar(recusa);
                await _senderRecusas.SendMessageAsync(mensagemDeRecusa, args.CancellationToken);
                transacao.Complete();
            }

            logger.LogWarning(
                "[{Instancia}] Proposta {PropostaId} recusada: idade {Idade} acima de {Maxima}. Recusa publicada em '{Topico}'.",
                _instancia,
                proposta.PropostaId,
                proposta.Idade,
                IdadeMaximaAceita,
                Topicos.PropostaRecusada);

            return;
        }

        // 4) Caminho feliz: simula a análise de risco, emite a apólice e publica o evento.
        await Task.Delay(TimeSpan.FromMilliseconds(500), args.CancellationToken);

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

        // Complete na fila + Send no tópico na MESMA transação do broker: ou os dois acontecem, ou nenhum.
        // Sem isso: publicar e cair antes do Complete → evento duplicado; completar e cair antes do Send → evento perdido.
        // Só vale para operações no Service Bus: um INSERT no SQL Server aqui dentro NÃO entraria na transação (fase 4: outbox).
        using (var transacao = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            // A primeira operação define a entidade "via" (a fila): o envio para o tópico é roteado por ela.
            await args.CompleteMessageAsync(mensagem, args.CancellationToken);
            var mensagemDaApolice = MensagensApolice.Criar(apolice);
            await _senderApolices.SendMessageAsync(mensagemDaApolice, args.CancellationToken);
            transacao.Complete();
        }

        atividade?.SetTag("vida.proposta.resultado", "aprovada");
        atividade?.SetTag("vida.apolice.id", apolice.ApoliceId);
        MetricasDeNegocio.PropostasDecididas.Add(
            1,
            new KeyValuePair<string, object?>("resultado", "aprovada"));

        var capital = proposta.CapitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "[{Instancia}] Proposta {PropostaId} aprovada: {Plano}, capital {Capital}, idade {Idade} (#{Seq}) → apólice {ApoliceId} publicada.",
            _instancia,
            proposta.PropostaId,
            proposta.Plano,
            capital,
            proposta.Idade,
            mensagem.SequenceNumber,
            apolice.ApoliceId);
    }

    /// <summary>
    /// Retry com espera: conclui a mensagem atual e agenda uma cópia para daqui a 2, 4, 8... segundos,
    /// na mesma transação (ou as duas coisas acontecem, ou nenhuma). Service Bus não tem backoff nativo
    /// no Abandon; o agendamento (ScheduledEnqueueTime) é a forma de esperar sem segurar o lock.
    /// </summary>
    private async Task AgendarNovaTentativa(ProcessMessageEventArgs args, PropostaSeguroVida proposta, int tentativa)
    {
        var mensagem = args.Message;
        if (tentativa >= MaximoDeTentativas)
        {
            await args.DeadLetterMessageAsync(
                mensagem,
                MotivosDeDeadLetter.TentativasEsgotadas,
                $"{tentativa} tentativas com backoff.",
                args.CancellationToken);

            logger.LogError(
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
        copia.ApplicationProperties[PropriedadesDaProposta.Tentativa] = tentativa + 1;

        using (var transacao = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await args.CompleteMessageAsync(mensagem, args.CancellationToken);
            // Send com ScheduledEnqueueTime = agendamento. ScheduleMessageAsync faz o mesmo e devolve o
            // SequenceNumber para cancelar depois (CancelScheduledMessageAsync).
            await _senderEntrada.SendMessageAsync(copia, args.CancellationToken);
            transacao.Complete();
        }

        logger.LogWarning(
            "[{Instancia}] Falha transitória na proposta {PropostaId} (tentativa {Tentativa}). Nova tentativa agendada para daqui a {Espera} s.",
            _instancia,
            proposta.PropostaId,
            tentativa,
            espera.TotalSeconds);
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
        await _senderApolices.DisposeAsync();
        await _senderRecusas.DisposeAsync();
        await _senderEntrada.DisposeAsync();
    }
}
