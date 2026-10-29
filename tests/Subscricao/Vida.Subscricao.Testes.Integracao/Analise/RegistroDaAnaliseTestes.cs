using Microsoft.EntityFrameworkCore;
using Vida.Propostas.Contratos.Dominio;
using Vida.Subscricao.Analise;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Testes.Integracao.Infra;

namespace Vida.Subscricao.Testes.Integracao.Analise;

/// <summary>
/// Inbox + negócio + outbox numa transação: o que entra na primeira entrega, e o que NÃO entra numa reentrega.
/// </summary>
[Collection(ColecaoDoBanco.Nome)]
public sealed class RegistroDaAnaliseTestes(BancoDeTeste bancoDeTeste)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aprovacao_grava_apolice_inbox_e_evento_no_outbox()
    {
        var apolice = NovaApolice();
        var messageId = apolice.PropostaId.ToString();

        await using (var banco = bancoDeTeste.NovoContexto())
        {
            var registro = new RegistroDaAnalise(banco);
            var gravou = await registro.RegistrarAprovacaoAsync(messageId, apolice, Cancelamento);
            Assert.True(gravou);
        }

        await using var leitura = bancoDeTeste.NovoContexto();
        var apoliceGravada = await leitura.Apolices.SingleAsync(a => a.ApoliceId == apolice.ApoliceId, Cancelamento);
        Assert.Equal(apolice.PropostaId, apoliceGravada.PropostaId);
        Assert.True(await leitura.Inbox.AnyAsync(m => m.MessageId == messageId, Cancelamento));
        var saida = await leitura.OutboxMensagens.SingleAsync(m => m.MessageId == apolice.ApoliceId.ToString(), Cancelamento);
        Assert.Equal(MensagensApolice.Topico, saida.Destino);
        Assert.Equal(MensagensApolice.Tipo, saida.Subject);
        Assert.Null(saida.PublicadaEm);
    }

    [Fact]
    public async Task Reentrega_da_mesma_mensagem_nao_grava_outra_apolice_nem_outro_evento()
    {
        var primeira = NovaApolice();
        var messageId = primeira.PropostaId.ToString();
        // Na reentrega, a análise roda de novo e gera outra ApoliceId: é a inbox que impede a duplicata.
        var reanalise = primeira with { ApoliceId = Guid.NewGuid() };

        await using (var banco = bancoDeTeste.NovoContexto())
        {
            var registro = new RegistroDaAnalise(banco);
            Assert.True(await registro.RegistrarAprovacaoAsync(messageId, primeira, Cancelamento));
        }
        bool gravouDeNovo;
        await using (var banco = bancoDeTeste.NovoContexto())
        {
            var registro = new RegistroDaAnalise(banco);
            gravouDeNovo = await registro.RegistrarAprovacaoAsync(messageId, reanalise, Cancelamento);
        }

        Assert.False(gravouDeNovo);
        await using var leitura = bancoDeTeste.NovoContexto();
        var apolices = await leitura.Apolices.CountAsync(a => a.PropostaId == primeira.PropostaId, Cancelamento);
        Assert.Equal(1, apolices);
        var eventos = await leitura.OutboxMensagens.CountAsync(m => m.CorrelationId == primeira.PropostaId.ToString(), Cancelamento);
        Assert.Equal(1, eventos);
    }

    [Fact]
    public async Task Recusa_grava_recusa_inbox_e_evento_no_outbox()
    {
        var recusa = new PropostaRecusada(
            PropostaId: Guid.NewGuid(),
            ClienteId: "CLI-1",
            Plano: Plano.Familiar,
            CapitalSegurado: 100_000m,
            Idade: 85,
            Motivo: "Idade 85 acima do limite de 80 anos.",
            RecusadaEm: DateTimeOffset.UtcNow);
        var messageId = recusa.PropostaId.ToString();

        await using (var banco = bancoDeTeste.NovoContexto())
        {
            var registro = new RegistroDaAnalise(banco);
            Assert.True(await registro.RegistrarRecusaAsync(messageId, recusa, Cancelamento));
        }

        await using var leitura = bancoDeTeste.NovoContexto();
        Assert.True(await leitura.Recusas.AnyAsync(r => r.PropostaId == recusa.PropostaId, Cancelamento));
        Assert.True(await leitura.Inbox.AnyAsync(m => m.MessageId == messageId, Cancelamento));
        var saida = await leitura.OutboxMensagens.SingleAsync(m => m.MessageId == recusa.PropostaId.ToString(), Cancelamento);
        Assert.Equal(MensagensRecusa.Topico, saida.Destino);
    }

    private static ApoliceEmitida NovaApolice() => new(
        ApoliceId: Guid.NewGuid(),
        PropostaId: Guid.NewGuid(),
        ClienteId: "CLI-2",
        Plano: Plano.Individual,
        CapitalSegurado: 300_000m,
        PremioMensal: 150m,
        EmitidaEm: DateTimeOffset.UtcNow,
        RequerRevisao: false);
}
