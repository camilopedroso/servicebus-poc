namespace Vida.Consumer.Endossos;

/// <summary>
/// O que o consumer guarda no session state de uma apólice. O broker armazena junto da sessão
/// (até o tamanho máximo de uma mensagem), então qualquer instância que pegar a sessão depois continua daqui.
/// </summary>
public sealed class EstadoDaSessao
{
    /// <summary>Última versão de endosso aplicada nesta apólice (0 = nenhuma).</summary>
    public int UltimaVersao { get; set; }

    /// <summary>
    /// Versões que chegaram adiantadas, com o SequenceNumber da mensagem adiada (deferred).
    /// Mensagem adiada só sai da fila buscada por esse número, então perdê-lo é perder a mensagem.
    /// </summary>
    public Dictionary<int, long> Adiadas { get; set; } = [];
}
