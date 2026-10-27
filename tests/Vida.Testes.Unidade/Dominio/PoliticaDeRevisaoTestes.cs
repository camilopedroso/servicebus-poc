using Vida.Mensageria.Dominio;

namespace Vida.Testes.Unidade.Dominio;

public class PoliticaDeRevisaoTestes
{
    [Theory]
    [InlineData(500_000, false)]
    [InlineData(1_000_000, false)] // o limite em si não exige revisão: a regra é "acima de"
    [InlineData(1_000_000.01, true)]
    [InlineData(1_500_000, true)]
    public void Revisao_so_acima_do_limite(decimal capital, bool esperado)
    {
        var requerRevisao = PoliticaDeRevisao.RequerRevisao(capital);

        Assert.Equal(esperado, requerRevisao);
    }
}
