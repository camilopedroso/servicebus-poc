using Vida.Subscricao.Contratos.Dominio;

namespace Vida.Testes.Unidade.Dominio;

public class CalculadoraDePremioTestes
{
    [Fact]
    public void Premio_cresce_com_a_idade()
    {
        var jovem = CalculadoraDePremio.CalcularMensal(300_000m, idade: 25);
        var idoso = CalculadoraDePremio.CalcularMensal(300_000m, idade: 70);

        Assert.True(idoso > jovem);
    }

    [Fact]
    public void Premio_tem_duas_casas_decimais()
    {
        // 123.457 × (0,0002 + 33 × 0,00001) = 65,43221 → 65,43
        var premio = CalculadoraDePremio.CalcularMensal(123_457m, idade: 33);

        Assert.Equal(65.43m, premio);
    }
}
