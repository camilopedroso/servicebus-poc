using Azure.Messaging.ServiceBus;
using Vida.Subscricao.Contratos.Comandos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Contratos.Topologia;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Fase 3: envia endossos para a fila "endossos" (sessions + detecção de duplicatas).
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class EnvioDeEndossos(ServiceBusClient client, ILogger<EnvioDeEndossos> logger) : IAsyncDisposable
{
    private static readonly string[] Tipos =
    [
        "Troca de beneficiário",
        "Aumento de capital",
        "Inclusão de cobertura",
        "Mudança de forma de pagamento",
        "Atualização cadastral"
    ];

    private readonly ServiceBusSender _sender = client.CreateSender(Filas.Endossos);

    /// <summary>
    /// Envia as versões 1..N de várias apólices, intercaladas (v1 de todas, depois v2 de todas...).
    /// </summary>
    /// <param name="apolices">Quantas apólices.</param>
    /// <param name="versoes">Quantos endossos por apólice.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Ids das apólices.</returns>
    public async Task<Guid[]> EnviarEmOrdemAsync(int apolices, int versoes, CancellationToken cancellationToken)
    {
        var ids = new Guid[apolices];
        for (var i = 0; i < apolices; i++)
        {
            ids[i] = Guid.NewGuid();
        }

        // Uma única chamada: a ordem dentro do lote é preservada no broker.
        var mensagens = new List<ServiceBusMessage>();
        for (var versao = 1; versao <= versoes; versao++)
        {
            foreach (var id in ids)
            {
                var endosso = NovoEndosso(id, versao);
                var mensagem = MensagensEndosso.Criar(endosso);
                mensagens.Add(mensagem);
            }
        }

        await _sender.SendMessagesAsync(mensagens, cancellationToken);

        foreach (var id in ids)
        {
            logger.LogInformation(
                "Apólice {ApoliceId}: endossos v1 a v{Versoes} enviados.",
                id,
                versoes);
        }

        logger.LogInformation(
            "{Total} endossos em '{Fila}', intercalados entre {Apolices} apólices.",
            mensagens.Count,
            Filas.Endossos,
            apolices);

        return ids;
    }

    /// <summary>
    /// Envia as versões de uma apólice embaralhadas, uma por chamada (a ordem de chegada é a ordem de envio).
    /// </summary>
    /// <param name="versoes">Quantos endossos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Id da apólice e a ordem em que as versões foram enviadas.</returns>
    public async Task<(Guid ApoliceId, int[] Ordem)> EnviarForaDeOrdemAsync(int versoes, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var emOrdem = Enumerable.Range(1, versoes);
        var ordem = emOrdem.ToArray();

        // Embaralha até sair de fato fora de ordem.
        while (ordem.SequenceEqual(emOrdem))
        {
            Random.Shared.Shuffle(ordem);
        }

        foreach (var versao in ordem)
        {
            var endosso = NovoEndosso(id, versao);
            var mensagem = MensagensEndosso.Criar(endosso);
            await _sender.SendMessageAsync(mensagem, cancellationToken);
        }

        var rotulos = ordem.Select(v => $"v{v}");
        var ordemDeEnvio = string.Join(", ", rotulos);
        logger.LogInformation(
            "Apólice {ApoliceId}: endossos enviados na ordem {Ordem}.",
            id,
            ordemDeEnvio);

        return (id, ordem);
    }

    /// <summary>
    /// Envia v1, a MESMA v1 de novo (mesmo MessageId) e v2. A detecção de duplicatas descarta a segunda v1:
    /// o envio dá certo para o producer, mas a cópia nunca chega à fila.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Id da apólice.</returns>
    public async Task<Guid> EnviarDuplicadoAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var v1 = NovoEndosso(id, 1);
        var v2 = NovoEndosso(id, 2);

        var primeiroEnvioV1 = MensagensEndosso.Criar(v1);
        await _sender.SendMessageAsync(primeiroEnvioV1, cancellationToken);

        // Simula o producer que não recebeu a confirmação (timeout) e reenvia. Mensagem nova, mesmo MessageId.
        var reenvioV1 = MensagensEndosso.Criar(v1);
        await _sender.SendMessageAsync(reenvioV1, cancellationToken);

        var envioV2 = MensagensEndosso.Criar(v2);
        await _sender.SendMessageAsync(envioV2, cancellationToken);

        logger.LogInformation(
            "Apólice {ApoliceId}: enviados v1, v1 de novo (MessageId {MessageId}) e v2. Os três envios foram aceitos.",
            id, $"{id}/v1");
        return id;
    }

    // Origem da versão: aqui o cenário inventa o número (1..N). Num sistema real ela viria do sistema de registro
    // da apólice (sequência de endossos por apólice, gerada na mesma transação que grava o endosso), para ser
    // estável entre reenvios. Destino: MensagensEndosso.Criar a põe no MessageId (dedup do broker) e o
    // ProcessadorDeEndossos a compara com a última versão aplicada (dedup e ordenação na aplicação).
    private static EndossoSolicitado NovoEndosso(Guid apoliceId, int versao) =>
        new(apoliceId, versao, Tipos[(versao - 1) % Tipos.Length], DateTimeOffset.UtcNow);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
