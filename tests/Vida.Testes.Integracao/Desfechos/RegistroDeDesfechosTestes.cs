using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vida.Propostas.Contratos.Dominio;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Desfechos;
using Vida.Testes.Integracao.Infra;

namespace Vida.Testes.Integracao.Desfechos;

/// <summary>
/// O desfecho da subscrição volta para a API: os eventos ApoliceEmitida e PropostaRecusada viram Aprovada e Recusada
/// no GET. Os eventos são aplicados direto pelo <see cref="RegistroDeDesfechos"/>, sem Service Bus (é o que o
/// ProcessadorDeDesfechos faz com cada mensagem), e o resultado é conferido pela API, como o cliente veria.
/// </summary>
[Collection(ColecaoDoBanco.Nome)]
public sealed class RegistroDeDesfechosTestes(FabricaDaApi fabrica)
{
    private readonly HttpClient _http = fabrica.CreateClient();

    private async Task<Guid> CriarProposta(int idade)
    {
        var corpo = new NovaProposta("CLI-1", idade, 300_000m, "Familiar");
        var resposta = await _http.PostAsJsonAsync("/api/propostas", corpo, TestContext.Current.CancellationToken);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        return criada!.PropostaId;
    }

    private async Task<PropostaResposta> Obter(Guid id)
    {
        var lida = await _http.GetFromJsonAsync<PropostaResposta>($"/api/propostas/{id}", TestContext.Current.CancellationToken);
        return lida!;
    }

    // Um escopo (e um DbContext) por evento, como o processor faz por mensagem.
    private async Task<ResultadoDoDesfecho> Aplicar(Func<BancoDePropostas, Task<ResultadoDoDesfecho>> aplicar)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        return await aplicar(banco);
    }

    private static PropostaRecusada Recusa(Guid propostaId) => new(
        propostaId, "CLI-1", Plano.Familiar, 300_000m, 93, "Idade 93 acima do limite de 80 anos.", DateTimeOffset.UtcNow);

    private static ApoliceEmitida Apolice(Guid propostaId) => new(
        Guid.NewGuid(), propostaId, "CLI-1", Plano.Familiar, 300_000m, 100m, DateTimeOffset.UtcNow, false);

    [Fact]
    public async Task Recusa_aparece_no_GET_com_o_motivo()
    {
        var id = await CriarProposta(idade: 93);
        var recusa = Recusa(id);

        var resultado = await Aplicar(banco => RegistroDeDesfechos.AplicarRecusaAsync(banco, recusa, TestContext.Current.CancellationToken));

        Assert.Equal(ResultadoDoDesfecho.Aplicado, resultado);
        var proposta = await Obter(id);
        Assert.Equal("Recusada", proposta.Status);
        Assert.Equal(recusa.Motivo, proposta.MotivoDaRecusa);
        Assert.NotNull(proposta.DecididaEm);
        Assert.Null(proposta.ApoliceId);
    }

    [Fact]
    public async Task Aprovacao_aparece_no_GET_com_a_apolice()
    {
        var id = await CriarProposta(idade: 40);
        var apolice = Apolice(id);

        var resultado = await Aplicar(banco => RegistroDeDesfechos.AplicarAprovacaoAsync(banco, apolice, TestContext.Current.CancellationToken));

        Assert.Equal(ResultadoDoDesfecho.Aplicado, resultado);
        var proposta = await Obter(id);
        Assert.Equal("Aprovada", proposta.Status);
        Assert.Equal(apolice.ApoliceId, proposta.ApoliceId);
        Assert.Null(proposta.MotivoDaRecusa);
    }

    [Fact]
    public async Task Evento_repetido_nao_muda_nada()
    {
        // At-least-once: o mesmo evento pode chegar duas vezes. A segunda entrega só confirma.
        var id = await CriarProposta(idade: 40);
        var apolice = Apolice(id);
        await Aplicar(banco => RegistroDeDesfechos.AplicarAprovacaoAsync(banco, apolice, TestContext.Current.CancellationToken));

        var repetido = await Aplicar(banco => RegistroDeDesfechos.AplicarAprovacaoAsync(banco, apolice, TestContext.Current.CancellationToken));

        Assert.Equal(ResultadoDoDesfecho.JaDecidida, repetido);
        var proposta = await Obter(id);
        Assert.Equal(apolice.ApoliceId, proposta.ApoliceId);
    }

    [Fact]
    public async Task Proposta_que_nao_veio_da_API_e_ignorada()
    {
        // Endpoints de demonstração enviam direto para a fila: o evento volta, mas a proposta não está no banco.
        var idDaDemonstracao = Guid.NewGuid();
        var recusa = Recusa(idDaDemonstracao);

        var resultado = await Aplicar(banco => RegistroDeDesfechos.AplicarRecusaAsync(banco, recusa, TestContext.Current.CancellationToken));

        Assert.Equal(ResultadoDoDesfecho.PropostaDesconhecida, resultado);
    }
}
