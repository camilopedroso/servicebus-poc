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

/// <summary>
/// Testes de integração do despachante do outbox: banco real (SQL Server do Testcontainers, via
/// <see cref="FabricaDaApi"/>) e broker falso (<see cref="ServiceBusFalso"/>). O que se prova aqui é o efeito nas
/// linhas depois do envio ou da falha: PublicadaEm, Tentativas, ProximaTentativaEm e o status da proposta.
/// <para>
/// A fábrica tira o despachante do host, para o polling de 1 s não disputar as linhas com as asserções. Cada teste
/// cria o seu e chama <see cref="DespachanteDoOutbox.DespacharLoteAsync"/> à mão (é internal; o csproj do Producer
/// tem InternalsVisibleTo para este projeto): uma rodada do loop, no momento que o teste escolhe.
/// </para>
/// <para>
/// O container é compartilhado com os outros testes da coleção, então um lote pode levar linhas pendentes deixadas
/// por eles. Por isso as asserções sempre filtram pelo id da proposta criada no próprio teste.
/// </para>
/// </summary>
[Collection(ColecaoDoBanco.Nome)]
public sealed class DespachanteDoOutboxTestes(FabricaDaApi fabrica)
{
    // O xUnit cria uma instância da classe por teste: cada teste começa com um broker falso limpo e no ar.
    private readonly ServiceBusFalso _serviceBus = new();

    // Mesmo construtor que o host usaria: escopos do container de DI da fábrica (cada lote abre o seu
    // BancoDePropostas), o broker falso no lugar do ServiceBusClient, e a simulação de indisponibilidade
    // desligada (quem desliga o broker nestes testes é o ServiceBusFalso.ForaDoAr).
    private DespachanteDoOutbox NovoDespachante()
    {
        var escopos = fabrica.Services.GetRequiredService<IServiceScopeFactory>();
        var simulacao = new SimulacaoDeIndisponibilidade();
        var logger = NullLogger<DespachanteDoOutbox>.Instance;
        return new DespachanteDoOutbox(escopos, _serviceBus, simulacao, logger);
    }

    // Arrange pela própria API, não por INSERT direto: a linha do outbox nasce como em produção, com a mensagem
    // serializada por MensagemDeSaida.De.
    private async Task<Guid> CriarProposta()
    {
        var http = fabrica.CreateClient();
        var corpo = new NovaProposta("CLI-1", 40, 300_000m, "Familiar");
        var resposta = await http.PostAsJsonAsync("/api/propostas", corpo, TestContext.Current.CancellationToken);
        var criada = await resposta.Content.ReadFromJsonAsync<PropostaResposta>(TestContext.Current.CancellationToken);
        return criada!.PropostaId;
    }

    // Escopo novo = DbContext novo: o que volta é o estado gravado no SQL Server, não o cache do EF.
    private async Task<(PropostaRegistrada Proposta, MensagemDeSaida Linha)> Ler(Guid id)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
        var messageId = id.ToString();
        var proposta = await banco.Propostas.SingleAsync(p => p.PropostaId == id, TestContext.Current.CancellationToken);
        var linha = await banco.OutboxMensagens.SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
        return (proposta, linha);
    }

    [Fact]
    public async Task Publica_a_pendente_e_marca_proposta_e_linha()
    {
        var id = await CriarProposta();
        var despachante = NovoDespachante();

        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        // Enviada uma vez só, para a fila de propostas, com o MessageId estável (o id da proposta).
        var messageId = id.ToString();
        var (destino, mensagem) = Assert.Single(_serviceBus.Enviadas, e => e.Mensagem.MessageId == messageId);
        Assert.Equal(Filas.Propostas, destino);
        // E marcada na mesma transação que segurou o lock: a linha sai das pendentes e a proposta avança.
        var (proposta, linha) = await Ler(id);
        Assert.NotNull(linha.PublicadaEm);
        Assert.Equal(StatusDaProposta.EnviadaParaAnalise, proposta.Status);
    }

    [Fact]
    public async Task Com_o_Service_Bus_fora_a_linha_fica_pendente_com_backoff()
    {
        var id = await CriarProposta();
        // O envio lança ServiceBusException(ServiceCommunicationProblem), como o SDK com o namespace inacessível.
        _serviceBus.ForaDoAr = true;
        var despachante = NovoDespachante();

        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        // A falha não perde nada: a linha continua pendente, conta a tentativa, agenda a próxima no futuro e guarda
        // o erro para diagnóstico. A proposta fica em Recebida até a publicação acontecer.
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
        // Primeira rodada com o broker fora: a linha ganha ProximaTentativaEm no futuro.
        var id = await CriarProposta();
        _serviceBus.ForaDoAr = true;
        var despachante = NovoDespachante();
        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        // Segunda rodada logo em seguida, com o broker de volta. Sem o filtro de ProximaTentativaEm na consulta
        // das pendentes, o despachante martelaria o broker a cada segundo durante uma queda.
        _serviceBus.ForaDoAr = false;
        await despachante.DespacharLoteAsync(TestContext.Current.CancellationToken);

        // ProximaTentativaEm ainda está no futuro (2 s): o despachante pula a linha.
        var messageId = id.ToString();
        Assert.DoesNotContain(_serviceBus.Enviadas, e => e.Mensagem.MessageId == messageId);
        var (_, linha) = await Ler(id);
        Assert.Null(linha.PublicadaEm);
    }
}
