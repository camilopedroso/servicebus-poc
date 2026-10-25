namespace Vida.Testes.Integracao.Infra;

/// <summary>
/// Todos os testes de integração dividem um container (subir o SQL Server leva ~10 s) e rodam em sequência:
/// o despachante processa todas as pendentes da tabela, e testes em paralelo se atrapalhariam.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDoBanco : ICollectionFixture<FabricaDaApi>
{
    public const string Nome = "Banco";
}
