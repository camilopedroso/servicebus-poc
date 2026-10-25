using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;

namespace Vida.Testes.Unidade.Mensagens;

/// <summary>
/// Os filtros das assinaturas só enxergam application properties, e o filtro SQL compara TIPOS:
/// requerRevisao = FALSE só casa com bool, capitalSegurado &gt; 1000000 só com número.
/// </summary>
public class MensagensApoliceTestes
{
    private static ApoliceEmitida Apolice(decimal capital, bool revisao) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "CLI-1", "Familiar", capital, 100m, DateTimeOffset.UtcNow, revisao);

    [Fact]
    public void Propriedades_de_roteamento_tem_o_tipo_que_o_filtro_espera()
    {
        var mensagem = MensagensApolice.Criar(Apolice(1_500_000m, revisao: true));

        Assert.IsType<bool>(mensagem.ApplicationProperties[Propriedades.RequerRevisao]);
        Assert.IsType<long>(mensagem.ApplicationProperties[Propriedades.CapitalSegurado]);
        Assert.Equal(true, mensagem.ApplicationProperties[Propriedades.RequerRevisao]);
        Assert.Equal(1_500_000L, mensagem.ApplicationProperties[Propriedades.CapitalSegurado]);
    }

    [Fact]
    public void Subject_e_o_tipo_do_evento()
    {
        var mensagem = MensagensApolice.Criar(Apolice(100_000m, revisao: false));

        Assert.Equal(TiposDeMensagem.ApoliceEmitida, mensagem.Subject);
    }
}
