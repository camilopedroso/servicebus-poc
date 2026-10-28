using System.Text.Json;
using Vida.Mensageria.Dominio;

namespace Vida.Testes.Unidade.Dominio;

public class PlanosTestes
{
    [Theory]
    [InlineData("Familiar")]
    [InlineData("familiar")]
    [InlineData("FAMILIAR")]
    [InlineData(" Familiar ")]
    public void Aceita_qualquer_grafia_e_devolve_o_enum(string texto)
    {
        var plano = Planos.Interpretar(texto);

        Assert.Equal(Plano.Familiar, plano);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ouro")]
    [InlineData("1")] // Enum.TryParse aceitaria: viraria Plano.Familiar
    [InlineData("7")] // Enum.TryParse aceitaria um valor fora do enum
    [InlineData("Individual, Familiar")] // Enum.TryParse aceitaria a lista
    public void Rejeita_o_que_nao_e_nome_de_plano(string? texto)
    {
        var plano = Planos.Interpretar(texto);

        Assert.Null(plano);
    }

    [Fact]
    public void Json_usa_o_nome_e_nao_o_numero()
    {
        var json = JsonSerializer.Serialize(Plano.Empresarial);

        Assert.Equal("\"Empresarial\"", json);
    }
}
