namespace Vida.Subscricao.Testes.Integracao.Infra;

/// <summary>
/// Os testes dividem um container (subir o SQL Server leva ~10 s). Cada teste usa ids próprios, então não se atrapalham.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDoBanco : ICollectionFixture<BancoDeTeste>
{
    public const string Nome = "Banco";
}
