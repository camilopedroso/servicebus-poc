using Vida.Subscricao.Contratos.Topologia;

namespace Vida.Testes.Unidade.Mensagens;

public class AssinaturasTestes
{
    [Theory]
    [InlineData("alto-valor")]
    [InlineData("ALTO-VALOR")]
    [InlineData(" Alto-Valor ")]
    public void Aceita_qualquer_grafia_e_devolve_o_nome_da_topologia(string texto)
    {
        var assinatura = Assinaturas.Interpretar(texto);

        Assert.Equal(Assinaturas.AltoValor, assinatura);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("altovalor")]
    public void Rejeita_nome_fora_da_topologia(string? texto)
    {
        var assinatura = Assinaturas.Interpretar(texto);

        Assert.Null(assinatura);
    }
}
