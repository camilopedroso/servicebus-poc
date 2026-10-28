using Vida.Mensageria.Dominio;
using System.Diagnostics;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;
using Vida.Mensageria.Topologia;
using Vida.Producer.Outbox;

namespace Vida.Testes.Unidade.Outbox;

public class MensagemDeSaidaTestes
{
    private static readonly PropostaSeguroVida Proposta =
        new(Guid.NewGuid(), "CLI-1", 40, 300_000m, Plano.Familiar, DateTimeOffset.UtcNow);

    [Fact]
    public void Ida_e_volta_preserva_ids_corpo_e_tipos_das_propriedades()
    {
        var original = MensagensProposta.Criar(Proposta, simularFalha: true);
        original.ApplicationProperties["numero"] = 42L;

        var linha = MensagemDeSaida.De(Filas.Propostas, original);
        var remontada = linha.ParaServiceBus();

        Assert.Equal(original.MessageId, remontada.MessageId);
        Assert.Equal(original.Subject, remontada.Subject);
        Assert.Equal(original.CorrelationId, remontada.CorrelationId);
        var corpoOriginal = original.Body.ToString();
        var corpoRemontado = remontada.Body.ToString();
        Assert.Equal(corpoOriginal, corpoRemontado);
        // JSON não guarda tipo .NET: sem a conversão, bool e número voltariam como string e os filtros SQL não casariam.
        Assert.Equal(true, remontada.ApplicationProperties[Propriedades.SimularFalha]);
        Assert.Equal(42L, remontada.ApplicationProperties["numero"]);
        Assert.Equal("Familiar", remontada.ApplicationProperties[Propriedades.Plano]);
    }

    [Fact]
    public void Grava_o_contexto_de_trace_do_momento_da_gravacao()
    {
        using var requisicao = new Activity("POST /api/propostas");
        requisicao.SetIdFormat(ActivityIdFormat.W3C);
        requisicao.Start();

        var mensagem = MensagensProposta.Criar(Proposta);
        var linha = MensagemDeSaida.De(Filas.Propostas, mensagem);
        // O despachante publica depois, dentro de outro span: o Diagnostic-Id tem de continuar sendo o da requisição.
        using var despachante = new Activity("PublicarOutbox");
        despachante.SetIdFormat(ActivityIdFormat.W3C);
        despachante.Start();
        var publicada = linha.ParaServiceBus();

        Assert.Equal(requisicao.Id, publicada.ApplicationProperties[MensagemDeSaida.PropriedadeDeTrace]);
    }

    [Fact]
    public void Sem_trace_ativo_nao_inventa_Diagnostic_Id()
    {
        Activity.Current = null;

        var mensagem = MensagensProposta.Criar(Proposta);
        var linha = MensagemDeSaida.De(Filas.Propostas, mensagem);
        var publicada = linha.ParaServiceBus();

        var temDiagnosticId = publicada.ApplicationProperties.ContainsKey(MensagemDeSaida.PropriedadeDeTrace);
        Assert.False(temDiagnosticId);
    }
}
