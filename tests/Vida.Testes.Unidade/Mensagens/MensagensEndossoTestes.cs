using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;

namespace Vida.Testes.Unidade.Mensagens;

public class MensagensEndossoTestes
{
    private static readonly Guid Apolice = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Sessao_e_a_apolice()
    {
        var endosso = new EndossoSolicitado(Apolice, 3, "Troca de beneficiário", DateTimeOffset.UtcNow);

        var mensagem = MensagensEndosso.Criar(endosso);

        var esperado = Apolice.ToString();
        Assert.Equal(esperado, mensagem.SessionId);
    }

    [Fact]
    public void Reenvio_da_mesma_versao_gera_o_mesmo_MessageId()
    {
        // É o que faz a detecção de duplicatas funcionar: o id vem do negócio, não de um Guid novo a cada envio.
        var agora = DateTimeOffset.UtcNow;
        var cincoSegundosDepois = agora.AddSeconds(5);
        var endosso = new EndossoSolicitado(Apolice, 2, "A", agora);
        var mesmoEndossoReenviado = new EndossoSolicitado(Apolice, 2, "A", cincoSegundosDepois);

        var primeiro = MensagensEndosso.Criar(endosso);
        var reenvio = MensagensEndosso.Criar(mesmoEndossoReenviado);

        Assert.Equal(primeiro.MessageId, reenvio.MessageId);
        Assert.Equal($"{Apolice}/v2", primeiro.MessageId);
    }

    [Fact]
    public void Versoes_diferentes_tem_MessageIds_diferentes()
    {
        var endossoV1 = new EndossoSolicitado(Apolice, 1, "A", DateTimeOffset.UtcNow);
        var endossoV2 = new EndossoSolicitado(Apolice, 2, "A", DateTimeOffset.UtcNow);

        var v1 = MensagensEndosso.Criar(endossoV1);
        var v2 = MensagensEndosso.Criar(endossoV2);

        Assert.NotEqual(v1.MessageId, v2.MessageId);
    }
}
