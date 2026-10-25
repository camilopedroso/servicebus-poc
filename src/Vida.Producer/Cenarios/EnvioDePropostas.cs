using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Dominio;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Fase 1: envia propostas para a fila "propostas", em lote ou uma de cada vez.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class EnvioDePropostas(ServiceBusClient client, ILogger<EnvioDePropostas> logger) : IAsyncDisposable
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    // Sender é thread-safe e mantém um link AMQP aberto: criar um por cenário reabriria o link a cada clique.
    private readonly ServiceBusSender _sender = client.CreateSender(Filas.Propostas);

    /// <summary>
    /// Envia um lote de propostas válidas para a fila.
    /// </summary>
    /// <param name="quantidade">Número de propostas a enviar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Ids das propostas enviadas.</returns>
    public async Task<Guid[]> EnviarLoteAsync(int quantidade, CancellationToken cancellationToken)
    {
        // Batch: várias mensagens numa única operação de rede. O SDK respeita o limite de tamanho
        // do tier (256 KB no Standard); TryAddMessage retorna false quando o lote enche.
        var enviadas = new List<Guid>(quantidade);
        while (enviadas.Count < quantidade)
        {
            using var lote = await _sender.CreateMessageBatchAsync(cancellationToken);
            var noLote = new List<PropostaSeguroVida>();
            while (enviadas.Count + noLote.Count < quantidade)
            {
                var proposta = NovaProposta();
                if (!lote.TryAddMessage(CriarMensagem(proposta, simularFalha: false)))
                {
                    break;
                }
                noLote.Add(proposta);
            }
            await _sender.SendMessagesAsync(lote, cancellationToken);
            logger.LogInformation("Lote enviado com {Quantidade} mensagem(ns).", lote.Count);
            foreach (var proposta in noLote)
            {
                logger.LogInformation(
                    "  proposta {PropostaId}: {Plano}, idade {Idade}, capital {Capital}.",
                    proposta.PropostaId, proposta.Plano, proposta.Idade, proposta.CapitalSegurado.ToString("C0", PtBr));
            }
            enviadas.AddRange(noLote.Select(p => p.PropostaId));
        }
        logger.LogInformation("Total: {Quantidade} proposta(s) em '{Fila}'.", enviadas.Count, Filas.Propostas);
        return [.. enviadas];
    }

    /// <summary>
    /// Envia uma única proposta para a fila.
    /// </summary>
    /// <param name="parametros">Idade, capital, plano e falha simulada; campos nulos são sorteados.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A proposta enviada.</returns>
    public async Task<PropostaSeguroVida> EnviarUmaAsync(EnvioDeProposta parametros, CancellationToken cancellationToken)
    {
        var proposta = NovaProposta(parametros.Idade, parametros.CapitalSegurado, parametros.Plano);
        await _sender.SendMessageAsync(CriarMensagem(proposta, parametros.SimularFalha), cancellationToken);
        logger.LogInformation(
            "Enviada proposta {PropostaId} ({Plano}, idade {Idade}, capital {Capital}, simularFalha={SimularFalha}).",
            proposta.PropostaId, proposta.Plano, proposta.Idade, proposta.CapitalSegurado.ToString("C0", PtBr), parametros.SimularFalha);
        return proposta;
    }

    /// <summary>
    /// Envia uma mensagem "venenosa": o corpo parece uma proposta, mas a idade vem como texto
    /// (ex.: integração com um sistema legado que mudou o formato). Nenhuma nova tentativa resolve.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>MessageId da mensagem enviada.</returns>
    public async Task<string> EnviarInvalidaAsync(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid().ToString();
        var corpo = $$"""
            {"PropostaId":"{{messageId}}","ClienteId":"CLI-{{Random.Shared.Next(1000, 9999)}}","Idade":"trinta e dois","CapitalSegurado":500000,"Plano":"{{Planos.Sortear()}}","CriadaEm":"{{DateTimeOffset.UtcNow:O}}"}
            """;
        var mensagem = new ServiceBusMessage(BinaryData.FromString(corpo))
        {
            MessageId = messageId,
            ContentType = "application/json",
            Subject = TiposDeMensagem.PropostaRecebida,
        };
        await _sender.SendMessageAsync(mensagem, cancellationToken);
        logger.LogInformation(
            "Enviada mensagem {MessageId} com corpo inválido (idade como texto): o Consumer não consegue ler.", messageId);
        return messageId;
    }

    /// <summary>
    /// Cria uma nova proposta de seguro de vida.
    /// </summary>
    /// <param name="idade">Idade do cliente (opcional; se não informado, será gerada aleatoriamente).</param>
    /// <param name="capital">Capital segurado (opcional; se não informado, será gerado aleatoriamente).</param>
    /// <param name="plano">Plano (opcional; se não informado, será sorteado).</param>
    /// <returns>Nova instância de PropostaSeguroVida.</returns>
    private static PropostaSeguroVida NovaProposta(int? idade = null, decimal? capital = null, string? plano = null) => new(
        PropostaId: Guid.NewGuid(),
        ClienteId: $"CLI-{Random.Shared.Next(1000, 9999)}",
        Idade: idade ?? Random.Shared.Next(18, 70),
        CapitalSegurado: capital ?? Random.Shared.Next(50, 2000) * 1_000m,
        Plano: plano ?? Planos.Sortear(),
        CriadaEm: DateTimeOffset.UtcNow);

    /// <summary>
    /// Cria uma mensagem do Service Bus a partir de uma proposta.
    /// </summary>
    /// <param name="proposta">Proposta de seguro de vida.</param>
    /// <param name="simularFalha">Se true, adiciona a propriedade SimularFalha na mensagem.</param>
    /// <returns>Nova instância de ServiceBusMessage.</returns>
    private static ServiceBusMessage CriarMensagem(PropostaSeguroVida proposta, bool simularFalha)
    {
        var mensagem = new ServiceBusMessage(BinaryData.FromObjectAsJson(proposta))
        {
            // MessageId = id de negócio: base para detecção de duplicatas (fase 3).
            MessageId = proposta.PropostaId.ToString(),
            ContentType = "application/json",
            // Subject (antigo "Label") descreve o tipo da mensagem; filtros de assinatura podem usar (fase 2).
            Subject = TiposDeMensagem.PropostaRecebida,
            CorrelationId = proposta.ClienteId,
        };
        mensagem.ApplicationProperties[Propriedades.Plano] = proposta.Plano;
        if (simularFalha)
        {
            mensagem.ApplicationProperties[Propriedades.SimularFalha] = true;
        }
        return mensagem;
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
