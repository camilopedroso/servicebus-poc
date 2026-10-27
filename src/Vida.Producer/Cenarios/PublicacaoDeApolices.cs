using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Dominio;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;

namespace Vida.Producer.Cenarios;

/// <summary>
/// Fase 2: publica ApoliceEmitida direto no tópico, sem passar pela fila. Serve para testar só os filtros.
/// </summary>
/// <param name="client">ServiceBusClient singleton do app.</param>
/// <param name="logger">Logger; o painel mostra estas linhas na coluna do Producer.</param>
public sealed class PublicacaoDeApolices(ServiceBusClient client, ILogger<PublicacaoDeApolices> logger) : IAsyncDisposable
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    private readonly ServiceBusSender _sender = client.CreateSender(Topicos.ApoliceEmitida);

    /// <summary>
    /// Publica uma apólice no tópico ApoliceEmitida.
    /// </summary>
    /// <param name="parametros">Capital e plano; campos nulos são sorteados.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A apólice publicada.</returns>
    public async Task<ApoliceEmitida> PublicarAsync(PublicacaoDeApolice parametros, CancellationToken cancellationToken)
    {
        var capitalSegurado = parametros.CapitalSegurado ?? Random.Shared.Next(50, 2000) * 1_000m;
        var numeroDoCliente = Random.Shared.Next(1000, 9999);
        var plano = parametros.Plano ?? Planos.Sortear();
        var premioMensal = CalculadoraDePremio.CalcularMensal(capitalSegurado, idade: 40);
        var requerRevisao = PoliticaDeRevisao.RequerRevisao(capitalSegurado);
        var apolice = new ApoliceEmitida(
            ApoliceId: Guid.NewGuid(),
            PropostaId: Guid.NewGuid(),
            ClienteId: $"CLI-{numeroDoCliente}",
            Plano: plano,
            CapitalSegurado: capitalSegurado,
            PremioMensal: premioMensal,
            EmitidaEm: DateTimeOffset.UtcNow,
            RequerRevisao: requerRevisao);

        var mensagem = MensagensApolice.Criar(apolice);
        await _sender.SendMessageAsync(mensagem, cancellationToken);
        var capital = capitalSegurado.ToString("C0", PtBr);
        logger.LogInformation(
            "Publicada apólice {ApoliceId} ({Plano}, capital {Capital}) em '{Topico}'.",
            apolice.ApoliceId, apolice.Plano, capital, Topicos.ApoliceEmitida);
        return apolice;
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
