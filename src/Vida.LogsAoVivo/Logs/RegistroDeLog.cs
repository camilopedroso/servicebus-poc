namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Uma linha de log como o painel recebe pelo stream SSE.
/// </summary>
/// <param name="Processo">Id gerado na partida do processo: o painel percebe quando o app foi reiniciado.</param>
/// <param name="Sequencia">Ordem do registro dentro do processo; serve para descartar repetidos ao reconectar.</param>
/// <param name="Momento">Quando o log foi escrito.</param>
/// <param name="Nivel">Nível do log (Information, Warning, Error...).</param>
/// <param name="Categoria">Categoria do logger, sem o namespace (ex.: Worker).</param>
/// <param name="Mensagem">Mensagem já formatada.</param>
/// <param name="Excecao">Tipo e mensagem da exceção, se houver.</param>
/// <param name="Ids">Ids de negócio extraídos dos placeholders do log (PropostaId, ApoliceId, MessageId).</param>
/// <param name="TraceId">Trace do OpenTelemetry ativo quando o log foi escrito (fase 5); o mesmo em todos os apps de um fluxo.</param>
public sealed record RegistroDeLog(
    Guid Processo,
    long Sequencia,
    DateTimeOffset Momento,
    string Nivel,
    string Categoria,
    string Mensagem,
    string? Excecao,
    IReadOnlyDictionary<string, string> Ids,
    string? TraceId = null);
