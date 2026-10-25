using Vida.Producer.Contratos;

namespace Vida.Testes.Unidade.Api;

public class NovaPropostaTestes
{
    [Fact]
    public void Corpo_completo_e_valido() =>
        Assert.Empty(new NovaProposta("CLI-1", 40, 300_000m, "Familiar").Validar());

    [Fact]
    public void Corpo_vazio_aponta_todos_os_campos()
    {
        var erros = new NovaProposta(null, null, null, null).Validar();

        Assert.Equal(
            ["CapitalSegurado", "ClienteId", "Idade", "Plano"],
            erros.Keys.Order());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(131)]
    public void Idade_fora_da_faixa_e_invalida(int idade) =>
        Assert.True(new NovaProposta("CLI-1", idade, 1m, "Familiar").Validar().ContainsKey("Idade"));

    [Fact]
    public void Plano_desconhecido_e_invalido() =>
        Assert.True(new NovaProposta("CLI-1", 40, 1m, "Ouro").Validar().ContainsKey("Plano"));
}
