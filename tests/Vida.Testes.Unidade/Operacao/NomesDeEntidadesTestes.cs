using Vida.Operacao.Ferramentas;

namespace Vida.Testes.Unidade.Operacao;

public class NomesDeEntidadesTestes
{
    private static readonly string[] Descobertas = ["alto-valor", "cobranca", "notificacao"];

    [Theory]
    [InlineData("alto-valor")]
    [InlineData("ALTO-VALOR")]
    [InlineData(" Alto-Valor ")]
    public void Aceita_qualquer_grafia_e_devolve_o_nome_do_broker(string texto)
    {
        var nome = NomesDeEntidades.Encontrar(texto, Descobertas);

        Assert.Equal("alto-valor", nome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("altovalor")]
    public void Rejeita_nome_que_nao_existe_no_broker(string? texto)
    {
        var nome = NomesDeEntidades.Encontrar(texto, Descobertas);

        Assert.Null(nome);
    }
}
