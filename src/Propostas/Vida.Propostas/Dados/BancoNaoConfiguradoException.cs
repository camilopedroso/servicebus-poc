namespace Vida.Propostas.Dados;

/// <summary>
/// A connection string do SQL Server não foi encontrada (src/sqlserver.local.json não existe ou não foi copiado).
/// O TratadorDeErros transforma em 503 com a instrução de como resolver.
/// </summary>
public sealed class BancoNaoConfiguradoException()
    : Exception("Connection string 'Propostas' não encontrada. Rode infra\\sql-up.ps1 e recompile (o arquivo é copiado no build).");
