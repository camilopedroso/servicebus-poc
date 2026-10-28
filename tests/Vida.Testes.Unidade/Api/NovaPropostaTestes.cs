using Vida.Producer.Contratos;

namespace Vida.Testes.Unidade.Api;

public class NovaPropostaTestes
{
    [Fact]
    public void Corpo_completo_e_valido()
    {
        var corpo = new NovaProposta("CLI-1", 40, 300_000m, "Familiar");

        var erros = corpo.Validar();

        Assert.Empty(erros);
    }

    [Fact]
    public void Corpo_vazio_aponta_todos_os_campos()
    {
        var corpo = new NovaProposta(null, null, null, null);

        var erros = corpo.Validar();

        var campos = erros.Keys.Order();
        Assert.Equal(["CapitalSegurado", "ClienteId", "Idade", "Plano"], campos);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(131)]
    public void Idade_fora_da_faixa_e_invalida(int idade)
    {
        var corpo = new NovaProposta("CLI-1", idade, 1m, "Familiar");

        var erros = corpo.Validar();

        Assert.True(erros.ContainsKey("Idade"));
    }

    [Fact]
    public void Plano_em_minusculas_e_valido()
    {
        var corpo = new NovaProposta("CLI-1", 40, 1m, "familiar");

        var erros = corpo.Validar();

        Assert.Empty(erros);
    }

    [Fact]
    public void Plano_desconhecido_e_invalido()
    {
        var corpo = new NovaProposta("CLI-1", 40, 1m, "Ouro");

        var erros = corpo.Validar();

        Assert.True(erros.ContainsKey("Plano"));
    }
}
