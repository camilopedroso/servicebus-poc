using Microsoft.EntityFrameworkCore;
using Vida.Propostas.Contratos.Dominio;
using Vida.Subscricao.Analise;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Endossos;
using Vida.Subscricao.Testes.Integracao.Infra;

namespace Vida.Subscricao.Testes.Integracao.Endossos;

/// <summary>
/// Pedido (versão gerada no banco + comando no outbox) e aplicação idempotente dos endossos.
/// </summary>
[Collection(ColecaoDoBanco.Nome)]
public sealed class RegistroDeEndossosTestes(BancoDeTeste bancoDeTeste)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedidos_recebem_versoes_sequenciais_e_vao_para_o_outbox_com_sessao()
    {
        var apoliceId = await EmitirApoliceAsync();

        var primeiro = await SolicitarAsync(apoliceId, "Troca de beneficiário", chave: null);
        var segundo = await SolicitarAsync(apoliceId, "Aumento de capital", chave: null);

        Assert.Equal(SituacaoDoPedidoDeEndosso.Registrado, primeiro.Situacao);
        Assert.Equal(1, primeiro.Endosso!.Versao);
        Assert.Equal(2, segundo.Endosso!.Versao);

        await using var leitura = bancoDeTeste.NovoContexto();
        var messageId = $"{apoliceId}/v2";
        var saida = await leitura.OutboxMensagens.SingleAsync(m => m.MessageId == messageId, Cancelamento);
        Assert.Equal(MensagensEndosso.Fila, saida.Destino);
        // A fila endossos exige sessão: sem o SessionId guardado, o despachante não conseguiria enviar.
        Assert.Equal(apoliceId.ToString(), saida.SessionId);
        var remontada = saida.ParaServiceBus();
        Assert.Equal(apoliceId.ToString(), remontada.SessionId);
    }

    [Fact]
    public async Task Mesma_chave_na_mesma_apolice_devolve_o_endosso_ja_pedido()
    {
        var apoliceId = await EmitirApoliceAsync();
        var chave = Guid.NewGuid().ToString();

        var primeiro = await SolicitarAsync(apoliceId, "Inclusão de cobertura", chave);
        var repetido = await SolicitarAsync(apoliceId, "Inclusão de cobertura", chave);

        Assert.Equal(SituacaoDoPedidoDeEndosso.Registrado, primeiro.Situacao);
        Assert.Equal(SituacaoDoPedidoDeEndosso.Repetido, repetido.Situacao);
        Assert.Equal(primeiro.Endosso!.Versao, repetido.Endosso!.Versao);
        await using var leitura = bancoDeTeste.NovoContexto();
        var endossos = await leitura.Endossos.CountAsync(e => e.ApoliceId == apoliceId, Cancelamento);
        Assert.Equal(1, endossos);
    }

    [Fact]
    public async Task Apolice_inexistente_nao_grava_nada()
    {
        var apoliceId = Guid.NewGuid();

        var pedido = await SolicitarAsync(apoliceId, "Atualização cadastral", chave: null);

        Assert.Equal(SituacaoDoPedidoDeEndosso.ApoliceInexistente, pedido.Situacao);
        Assert.Null(pedido.Endosso);
        await using var leitura = bancoDeTeste.NovoContexto();
        var destino = MensagensEndosso.Fila;
        var sessao = apoliceId.ToString();
        var naSaida = await leitura.OutboxMensagens.AnyAsync(m => m.Destino == destino && m.SessionId == sessao, Cancelamento);
        Assert.False(naSaida);
    }

    [Fact]
    public async Task Aplicar_avanca_a_versao_uma_vez_so()
    {
        var apoliceId = await EmitirApoliceAsync();
        await SolicitarAsync(apoliceId, "Troca de beneficiário", chave: null);

        var primeira = await AplicarAsync(apoliceId, 1);
        // Reentrega da mesma versão (ex.: caiu entre gravar e concluir): não muda nada.
        var reentrega = await AplicarAsync(apoliceId, 1);

        Assert.True(primeira);
        Assert.False(reentrega);
        await using var leitura = bancoDeTeste.NovoContexto();
        var apolice = await leitura.Apolices.SingleAsync(a => a.ApoliceId == apoliceId, Cancelamento);
        Assert.Equal(1, apolice.VersaoDosEndossos);
        var endosso = await leitura.Endossos.SingleAsync(e => e.ApoliceId == apoliceId && e.Versao == 1, Cancelamento);
        Assert.NotNull(endosso.AplicadoEm);
    }

    [Fact]
    public async Task Versao_fora_de_ordem_nao_e_aplicada()
    {
        var apoliceId = await EmitirApoliceAsync();
        await SolicitarAsync(apoliceId, "Troca de beneficiário", chave: null);
        await SolicitarAsync(apoliceId, "Aumento de capital", chave: null);

        // A v2 antes da v1: o ProcessadorDeEndossos adia pela sessão, e o banco também não pula versão.
        var aplicou = await AplicarAsync(apoliceId, 2);

        Assert.False(aplicou);
        await using var leitura = bancoDeTeste.NovoContexto();
        var apolice = await leitura.Apolices.SingleAsync(a => a.ApoliceId == apoliceId, Cancelamento);
        Assert.Equal(0, apolice.VersaoDosEndossos);
    }

    private async Task<PedidoDeEndosso> SolicitarAsync(Guid apoliceId, string tipo, string? chave)
    {
        await using var banco = bancoDeTeste.NovoContexto();
        var registro = new RegistroDeEndossos(banco);
        return await registro.SolicitarAsync(apoliceId, tipo, chave, Cancelamento);
    }

    private async Task<bool> AplicarAsync(Guid apoliceId, int versao)
    {
        await using var banco = bancoDeTeste.NovoContexto();
        var registro = new RegistroDeEndossos(banco);
        return await registro.AplicarAsync(apoliceId, versao, Cancelamento);
    }

    // A apólice nasce como em produção: pela análise aprovada (RegistroDaAnalise).
    private async Task<Guid> EmitirApoliceAsync()
    {
        var apolice = new ApoliceEmitida(
            ApoliceId: Guid.NewGuid(),
            PropostaId: Guid.NewGuid(),
            ClienteId: "CLI-3",
            Plano: Plano.Familiar,
            CapitalSegurado: 200_000m,
            PremioMensal: 90m,
            EmitidaEm: DateTimeOffset.UtcNow,
            RequerRevisao: false);
        await using var banco = bancoDeTeste.NovoContexto();
        var registro = new RegistroDaAnalise(banco);
        await registro.RegistrarAprovacaoAsync(apolice.PropostaId.ToString(), apolice, Cancelamento);
        return apolice.ApoliceId;
    }
}
