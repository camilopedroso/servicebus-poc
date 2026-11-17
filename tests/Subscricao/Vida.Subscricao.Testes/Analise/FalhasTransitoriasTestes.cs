using Microsoft.EntityFrameworkCore;
using Vida.Subscricao.Analise;

namespace Vida.Subscricao.Testes.Analise;

public class FalhasTransitoriasTestes
{
    [Theory]
    [InlineData(-2)]    // timeout
    [InlineData(53)]    // servidor inacessível
    [InlineData(10061)] // conexão recusada (container parado)
    [InlineData(1205)]  // deadlock
    [InlineData(40613)] // Azure SQL indisponível
    public void Numeros_de_falha_passageira_sao_transitorios(int numero)
    {
        var transitorio = FalhasTransitorias.EhNumeroTransitorio(numero);

        Assert.True(transitorio);
    }

    [Theory]
    [InlineData(2627)]  // chave primária duplicada: repetir dá o mesmo erro
    [InlineData(547)]   // violação de foreign key
    [InlineData(8152)]  // texto maior que a coluna
    public void Erros_de_dado_nao_sao_transitorios(int numero)
    {
        var transitorio = FalhasTransitorias.EhNumeroTransitorio(numero);

        Assert.False(transitorio);
    }

    [Fact]
    public void Timeout_e_transitorio()
    {
        var transitorio = FalhasTransitorias.EhTransitoria(new TimeoutException());

        Assert.True(transitorio);
    }

    [Fact]
    public void Timeout_dentro_do_DbUpdateException_e_transitorio()
    {
        // O EF embrulha a falha do provider: a decisão olha a exceção de dentro.
        var excecao = new DbUpdateException("falhou ao salvar", new TimeoutException());

        var transitorio = FalhasTransitorias.EhTransitoria(excecao);

        Assert.True(transitorio);
    }

    [Fact]
    public void Falha_transitoria_embrulhada_pelo_EF_e_transitoria()
    {
        // Como a estratégia de execução padrão do EF Core para SQL Server entrega a falha transitória.
        var excecao = new InvalidOperationException("likely due to a transient failure", new TimeoutException());

        var transitorio = FalhasTransitorias.EhTransitoria(excecao);

        Assert.True(transitorio);
    }

    [Fact]
    public void Erro_de_programacao_nao_e_transitorio()
    {
        var transitorio = FalhasTransitorias.EhTransitoria(new InvalidOperationException());

        Assert.False(transitorio);
    }
}
