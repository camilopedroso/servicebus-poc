using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vida.Propostas.Contratos.Dominio;
using Vida.Propostas.Controllers;
using Vida.Propostas.Contratos;
using Vida.Propostas.Dados;
using Vida.Testes.Integracao.Infra;
using Vida.Propostas.Contratos.Mensagens;

namespace Vida.Testes.Integracao.Api;

/// <summary>
/// Testes de integração da API de propostas: o serviço de Propostas inteiro roda em memória (WebApplicationFactory), com o
/// mesmo Program.cs, os mesmos controllers e o mesmo BancoDePropostas do desenvolvimento. O que muda é só o banco:
/// um SQL Server descartável em Docker (Testcontainers), com a mesma imagem do infra/sql-up.ps1. Na partida, o
/// serviço aplica as migrations nele, então o schema testado é o que as migrations produzem, não uma cópia.
/// <para>
/// Por que não SQLite ou o provider InMemory: o código depende de recursos do SQL Server que eles não têm, ou
/// emulam de outro jeito: hints de lock no despachante (UPDLOCK, READPAST), índice único filtrado da
/// Idempotency-Key, schema Propostas, migrations geradas para SQL Server. Um teste verde no SQLite não provaria
/// que isso funciona.
/// </para>
/// </summary>
[Collection(ColecaoDoBanco.Nome)]
public sealed class ApiDePropostasTestes(FabricaDaApi fabrica)
{
    private readonly HttpClient _http = fabrica.CreateClient();

    private static NovaProposta Valida() => new("CLI-1", 40, 300_000m, "Familiar");

    [Fact]
    public async Task Post_grava_proposta_e_mensagem_do_outbox_juntas()
    {
        // Requisição HTTP de verdade, mas em memória (sem porta aberta): passa por roteamento, validação e
        // PropostasController, que grava proposta e mensagem do outbox num único SaveChanges no container.
        var corpo = Valida();
        var resposta = await _http.PostAsJsonAsync("/api/propostas", corpo, TestContext.Current.CancellationToken);

        // O contrato HTTP: 201 + Location apontando para o GET, e a proposta ainda só "Recebida".
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        Assert.Equal($"/api/propostas/{criada!.PropostaId}", resposta.Headers.Location!.OriginalString);
        Assert.Equal("Recebida", criada.Status);

        // O efeito no banco: um escopo novo dá um DbContext novo, então a linha vem do SQL Server, não do cache
        // do EF. Se o SELECT acha a mensagem, ela foi gravada junto com a proposta.
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var messageId = criada.PropostaId.ToString();
        var linha = await banco.OutboxMensagens.SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
        Assert.Equal(MensagensProposta.Topico, linha.Destino);
        // Ainda não publicada: a API não fala com o Service Bus, e o despachante foi tirado do host na FabricaDaApi.
        Assert.Null(linha.PublicadaEm);
    }

    [Fact]
    public async Task Get_devolve_a_proposta_criada()
    {
        var corpo = Valida();
        var resposta = await _http.PostAsJsonAsync("/api/propostas", corpo, TestContext.Current.CancellationToken);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);

        var lida = await _http.GetFromJsonAsync<PropostaResposta>(
            $"/api/propostas/{criada!.PropostaId}", TestContext.Current.CancellationToken);

        Assert.Equal(criada.PropostaId, lida!.PropostaId);
    }

    [Fact]
    public async Task Get_de_id_inexistente_e_404()
    {
        var idInexistente = Guid.NewGuid();
        var resposta = await _http.GetAsync($"/api/propostas/{idInexistente}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Corpo_invalido_e_400_sem_gravar_nada()
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var antes = await banco.Propostas.CountAsync(TestContext.Current.CancellationToken);

        var corpoInvalido = new NovaProposta(null, 200, null, "Ouro");
        var resposta = await _http.PostAsJsonAsync("/api/propostas", corpoInvalido, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var depois = await banco.Propostas.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(antes, depois);
    }

    [Fact]
    public async Task Idempotency_Key_repetida_devolve_a_mesma_proposta()
    {
        var guid = Guid.NewGuid();
        var chave = guid.ToString();

        var primeira = await PostComChave(chave);
        var segunda = await PostComChave(chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var cabecalho = segunda.Headers.GetValues("Idempotent-Replayed");
        var repetida = cabecalho.Single();
        Assert.Equal("true", repetida);
        var a = await primeira.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        var b = await segunda.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        Assert.Equal(a!.PropostaId, b!.PropostaId);
        var mensagensNoOutbox = await ContarMensagensDoOutbox(a.PropostaId);
        Assert.Equal(1, mensagensNoOutbox);
    }

    [Fact]
    public async Task Posts_simultaneos_com_a_mesma_chave_criam_uma_proposta_so()
    {
        // A busca pela chave não basta: dois POSTs podem passar por ela ao mesmo tempo. O índice único decide.
        var guid = Guid.NewGuid();
        var chave = guid.ToString();

        // Dispara os 10 POSTs antes de esperar qualquer um: é isso que os faz concorrer.
        var envios = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 10; i++)
        {
            var envio = PostComChave(chave);
            envios.Add(envio);
        }
        var respostas = await Task.WhenAll(envios);

        Assert.All(respostas, r => Assert.True(r.IsSuccessStatusCode, $"Status {r.StatusCode}"));
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var gravadas = await banco.Propostas.CountAsync(p => p.ChaveDeIdempotencia == chave, TestContext.Current.CancellationToken);
        Assert.Equal(1, gravadas);
    }

    [Fact]
    public async Task Entrada_tolerante_sai_canonica()
    {
        // Lei de Postel: aceita grafia e espaços soltos, mas grava e devolve a forma canônica.
        var corpo = new NovaProposta("  CLI-7  ", 40, 300_000m, " FAMILIAR ");

        var resposta = await _http.PostAsJsonAsync("/api/propostas", corpo, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        Assert.Equal("CLI-7", criada!.ClienteId);
        Assert.Equal(Plano.Familiar, criada.Plano);
    }

    [Fact]
    public async Task Idempotency_Key_maior_que_a_coluna_e_400_e_nao_500()
    {
        var chave = new string('x', PropostaRegistrada.TamanhoMaximoDaChave + 1);

        var resposta = await PostComChave(chave);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Idempotency_Key_com_espacos_e_a_mesma_chave()
    {
        var guid = Guid.NewGuid();
        var chave = guid.ToString();

        var primeira = await PostComChave(chave);
        var segunda = await PostComChave($"  {chave}  ");

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
    }

    private Task<HttpResponseMessage> PostComChave(string chave)
    {
        var corpo = Valida();
        var conteudo = JsonContent.Create(corpo);
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/propostas") { Content = conteudo };
        requisicao.Headers.Add(PropostasController.CabecalhoDeIdempotencia, chave);
        return _http.SendAsync(requisicao, TestContext.Current.CancellationToken);
    }

    private async Task<int> ContarMensagensDoOutbox(Guid propostaId)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var messageId = propostaId.ToString();
        return await banco.OutboxMensagens.CountAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
    }
}
