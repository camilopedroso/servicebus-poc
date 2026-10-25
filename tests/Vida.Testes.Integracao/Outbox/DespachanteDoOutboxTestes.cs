using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vida.Mensageria.Topologia;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;
using Vida.Testes.Integracao.Infra;

namespace Vida.Testes.Integracao.Outbox;

[Collection(ColecaoDoBanco.Nome)]
public sealed class DespachanteDoOutboxTestes(FabricaDaApi fabrica)
{
    private readonly ServiceBusFalso _serviceBus = new();

    private DespachanteDoOutbox NovoDespachante() => new(
        fabrica.Services.GetRequiredService<IServiceScopeFactory>(),
        _serviceBus,
        new SimulacaoDeIndisponibilidade(),
        NullLogger<DespachanteDoOutbox>.Instance);

    private async Task<Guid> CriarProposta()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync(
            "/api/propostas", new NovaProposta("CLI-1", 40, 300_000m, "Familiar"), TestContext.Current.CancellationToken);
        return (await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken))!.PropostaId;
    }

    private async Task<(PropostaRegistrada Proposta, MensagemDeSaida Linha)> Ler(Guid id)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        return (await banco.Propostas.SingleAsync(p => p.PropostaId == id, TestContext.Current.CancellationToken),
            await banco.OutboxMensagens.SingleAsync(m => m.MessageId == id.ToString(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Publica_a_pendente_e_marca_proposta_e_linha()
    {
        var id = await CriarProposta();

        await NovoDespachante().DespacharLoteAsync(TestContext.Current.CancellationToken);

        var (destino, mensagem) = Assert.Single(_serviceBus.Enviadas, e => e.Mensagem.MessageId == id.ToString());
        Assert.Equal(Filas.Propostas, destino);
        var (proposta, linha) = await Ler(id);
        Assert.NotNull(linha.PublicadaEm);
        Assert.Equal(StatusDaProposta.EnviadaParaAnalise, proposta.Status);
    }

    [Fact]
    public async Task Com_o_Service_Bus_fora_a_linha_fica_pendente_com_backoff()
    {
        var id = await CriarProposta();
        _serviceBus.ForaDoAr = true;

        await NovoDespachante().DespacharLoteAsync(TestContext.Current.CancellationToken);

        var (proposta, linha) = await Ler(id);
        Assert.Null(linha.PublicadaEm);
        Assert.Equal(1, linha.Tentativas);
        Assert.True(linha.ProximaTentativaEm > DateTimeOffset.UtcNow);
        Assert.Contains("ServiceCommunicationProblem", linha.UltimoErro);
        Assert.Equal(StatusDaProposta.Recebida, proposta.Status);
    }

    [Fact]
    public async Task Linha_em_backoff_nao_e_reenviada_antes_da_hora()
    {
        var id = await CriarProposta();
        _serviceBus.ForaDoAr = true;
        var despachante = NovoDespachante();
        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        _serviceBus.ForaDoAr = false;
        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        // ProximaTentativaEm ainda está no futuro (2 s): o despachante pula a linha.
        Assert.DoesNotContain(_serviceBus.Enviadas, e => e.Mensagem.MessageId == id.ToString());
        Assert.Null((await Ler(id)).Linha.PublicadaEm);
    }
}
