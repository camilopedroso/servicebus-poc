using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Vida.Subscricao.Dados;

namespace Vida.Subscricao.Testes.Integracao.Infra;

/// <summary>
/// Um SQL Server descartável (Testcontainers) com as migrations da Subscrição aplicadas. Não sobe o serviço: os testes
/// usam o RegistroDaAnalise direto, que é onde mora a regra da inbox. Mesma imagem do infra/sql-up.ps1.
/// </summary>
public sealed class BancoDeTeste : IAsyncLifetime
{
    private readonly MsSqlContainer _sql;

    public BancoDeTeste()
    {
        var construtor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest");
        _sql = construtor.Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();
        await using var banco = NovoContexto();
        await banco.Database.MigrateAsync();
    }

    /// <summary>
    /// Um DbContext novo, como o serviço usa um por mensagem: o que um teste vê vem do banco, não do change tracker.
    /// </summary>
    /// <returns>Contexto ligado ao container.</returns>
    public BancoDaSubscricao NovoContexto()
    {
        var conexao = _sql.GetConnectionString();
        var opcoes = new DbContextOptionsBuilder<BancoDaSubscricao>()
            .UseSqlServer(conexao)
            .Options;
        return new BancoDaSubscricao(opcoes);
    }

    public async ValueTask DisposeAsync() => await _sql.DisposeAsync();
}
