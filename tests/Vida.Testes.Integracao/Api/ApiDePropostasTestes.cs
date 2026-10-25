using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vida.Mensageria.Topologia;
using Vida.Producer.Api;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Testes.Integracao.Infra;

namespace Vida.Testes.Integracao.Api;

[Collection(ColecaoDoBanco.Nome)]
public sealed class ApiDePropostasTestes(FabricaDaApi fabrica)
{
    private readonly HttpClient _http = fabrica.CreateClient();

    private static NovaProposta Valida() => new("CLI-1", 40, 300_000m, "Familiar");

    [Fact]
    public async Task Post_grava_proposta_e_mensagem_do_outbox_juntas()
    {
        var resposta = await _http.PostAsJsonAsync("/api/propostas", Valida(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        Assert.Equal($"/api/propostas/{criada!.PropostaId}", resposta.Headers.Location!.OriginalString);
        Assert.Equal("Recebida", criada.Status);

        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var linha = await banco.OutboxMensagens.SingleAsync(
            m => m.MessageId == criada.PropostaId.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(Filas.Propostas, linha.Destino);
        Assert.Null(linha.PublicadaEm);
    }

    [Fact]
    public async Task Get_devolve_a_proposta_criada()
    {
        var criada = await (await _http.PostAsJsonAsync("/api/propostas", Valida(), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);

        var lida = await _http.GetFromJsonAsync<PropostaResposta>(
            $"/api/propostas/{criada!.PropostaId}", TestContext.Current.CancellationToken);

        Assert.Equal(criada.PropostaId, lida!.PropostaId);
    }

    [Fact]
    public async Task Get_de_id_inexistente_e_404()
    {
        var resposta = await _http.GetAsync($"/api/propostas/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Corpo_invalido_e_400_sem_gravar_nada()
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var antes = await banco.Propostas.CountAsync(TestContext.Current.CancellationToken);

        var resposta = await _http.PostAsJsonAsync(
            "/api/propostas", new NovaProposta(null, 200, null, "Ouro"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(antes, await banco.Propostas.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Idempotency_Key_repetida_devolve_a_mesma_proposta()
    {
        var chave = Guid.NewGuid().ToString();

        var primeira = await PostComChave(chave);
        var segunda = await PostComChave(chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal("true", segunda.Headers.GetValues("Idempotent-Replayed").Single());
        var a = await primeira.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        var b = await segunda.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        Assert.Equal(a!.PropostaId, b!.PropostaId);
        Assert.Equal(1, await ContarMensagensDoOutbox(a.PropostaId));
    }

    [Fact]
    public async Task Posts_simultaneos_com_a_mesma_chave_criam_uma_proposta_so()
    {
        // A busca pela chave não basta: dois POSTs podem passar por ela ao mesmo tempo. O índice único decide.
        var chave = Guid.NewGuid().ToString();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostComChave(chave)));

        Assert.All(respostas, r => Assert.True(r.IsSuccessStatusCode, $"Status {r.StatusCode}"));
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        Assert.Equal(1, await banco.Propostas.CountAsync(p => p.ChaveDeIdempotencia == chave, TestContext.Current.CancellationToken));
    }

    private Task<HttpResponseMessage> PostComChave(string chave)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/propostas") { Content = JsonContent.Create(Valida()) };
        requisicao.Headers.Add(EndpointsDePropostas.CabecalhoDeIdempotencia, chave);
        return _http.SendAsync(requisicao, TestContext.Current.CancellationToken);
    }

    private async Task<int> ContarMensagensDoOutbox(Guid propostaId)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        return await banco.OutboxMensagens.CountAsync(m => m.MessageId == propostaId.ToString(), TestContext.Current.CancellationToken);
    }
}
