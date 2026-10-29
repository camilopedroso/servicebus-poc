using Vida.Propostas.Contratos.Dominio;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;

namespace Vida.Subscricao.Testes.Mensagens;

/// <summary>
/// Os filtros das assinaturas só enxergam application properties, e o filtro SQL compara TIPOS:
/// requerRevisao = FALSE só casa com bool, capitalSegurado &gt; 1000000 só com número.
/// </summary>
public class MensagensApoliceTestes
{
    private static ApoliceEmitida Apolice(decimal capital, bool revisao) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "CLI-1", Plano.Familiar, capital, 100m, DateTimeOffset.UtcNow, revisao);

    [Fact]
    public void Propriedades_de_roteamento_tem_o_tipo_que_o_filtro_espera()
    {
        var apolice = Apolice(1_500_000m, revisao: true);

        var mensagem = MensagensApolice.Criar(apolice);


        Assert.IsType<bool>(mensagem.ApplicationProperties[PropriedadesDeRoteamento.RequerRevisao]);
        Assert.IsType<long>(mensagem.ApplicationProperties[PropriedadesDeRoteamento.CapitalSegurado]);
        Assert.Equal(true, mensagem.ApplicationProperties[PropriedadesDeRoteamento.RequerRevisao]);
        Assert.Equal(1_500_000L, mensagem.ApplicationProperties[PropriedadesDeRoteamento.CapitalSegurado]);
    }

    [Fact]
    public void Subject_e_o_tipo_do_evento()
    {
        var apolice = Apolice(100_000m, revisao: false);

        var mensagem = MensagensApolice.Criar(apolice);

        Assert.Equal(MensagensApolice.Tipo, mensagem.Subject);
    }
}
