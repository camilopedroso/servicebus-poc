namespace Vida.Consumer.Endossos;

/// <summary>
/// O que fazer com um endosso que chegou, dada a última versão aplicada na apólice.
/// </summary>
public enum AcaoDoEndosso
{
    /// <summary>É a próxima versão: aplica.</summary>
    Aplicar,

    /// <summary>Chegou antes da hora (falta uma versão anterior): adia (defer).</summary>
    Adiar,

    /// <summary>Versão já aplicada (reenvio ou reentrega): conclui sem reaplicar.</summary>
    IgnorarJaAplicado,
}
