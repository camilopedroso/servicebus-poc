using Vida.Subscricao.Endossos;

namespace Vida.Subscricao.Testes.Endossos;

public class DecisaoDeEndossoTestes
{
    [Theory]
    [InlineData(1, 0, AcaoDoEndosso.Aplicar)]           // primeira versão de uma apólice nova
    [InlineData(4, 3, AcaoDoEndosso.Aplicar)]           // a próxima da fila
    [InlineData(3, 1, AcaoDoEndosso.Adiar)]             // pulou a v2: espera
    [InlineData(5, 0, AcaoDoEndosso.Adiar)]
    [InlineData(2, 2, AcaoDoEndosso.IgnorarJaAplicado)] // reentrega da última aplicada
    [InlineData(1, 4, AcaoDoEndosso.IgnorarJaAplicado)] // reenvio antigo, fora da janela de duplicatas
    public void Decide_pela_ultima_versao_aplicada(int versao, int ultima, AcaoDoEndosso esperado)
    {
        var acao = DecisaoDeEndosso.Decidir(versao, ultima);

        Assert.Equal(esperado, acao);
    }

    [Fact]
    public void Sequencia_embaralhada_termina_aplicada_em_ordem()
    {
        // Simula o processador: a ordem de chegada v2, v4, v1, v5, v3 vira a ordem de aplicação v1..v5.
        var aplicadas = new List<int>();
        var adiadas = new SortedSet<int>();
        var ultima = 0;
        int[] ordemDeChegada = [2, 4, 1, 5, 3];
        foreach (var versao in ordemDeChegada)
        {
            var acao = DecisaoDeEndosso.Decidir(versao, ultima);
            if (acao == AcaoDoEndosso.Adiar)
            {
                adiadas.Add(versao);
                continue;
            }
            ultima = versao;
            aplicadas.Add(ultima);
            while (adiadas.Remove(ultima + 1))
            {
                ultima++;
                aplicadas.Add(ultima);
            }
        }

        Assert.Equal([1, 2, 3, 4, 5], aplicadas);
        Assert.Empty(adiadas);
    }
}
