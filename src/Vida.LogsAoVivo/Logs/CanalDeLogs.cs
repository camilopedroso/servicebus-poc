using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Vida.LogsAoVivo.Logs;

/// <summary>
/// Guarda os últimos registros e repassa cada novo registro a todos os painéis conectados.
/// Quem conecta depois recebe o histórico primeiro, então reabrir o painel não perde o que já aconteceu.
/// </summary>
public sealed class CanalDeLogs
{
    private const int TamanhoDoHistorico = 500;

    private readonly Queue<RegistroDeLog> _historico = new();
    private readonly List<Channel<RegistroDeLog>> _assinantes = [];
    private readonly Lock _trava = new();
    private long _sequencia;

    /// <summary>Identifica esta execução do processo.</summary>
    public Guid Processo { get; } = Guid.NewGuid();

    /// <summary>
    /// Numera o registro e o publica no histórico e para todos os painéis conectados.
    /// </summary>
    /// <param name="registro">Registro de log; a Sequencia recebida é ignorada.</param>
    public void Publicar(RegistroDeLog registro)
    {
        lock (_trava)
        {
            // Numera sob a mesma trava que publica: a ordem da sequência é a ordem de entrega.
            // Numerando fora dela, duas threads podiam publicar 11 antes de 10, e o painel,
            // que descarta sequência <= última vista (para ignorar o histórico ao reconectar), sumia com a 10.
            registro = registro with { Sequencia = ++_sequencia };
            _historico.Enqueue(registro);
            if (_historico.Count > TamanhoDoHistorico)
            {
                _historico.Dequeue();
            }
            foreach (var canal in _assinantes)
            {
                // Canal limitado com DropOldest: um painel lento perde linhas antigas, mas nunca trava quem loga.
                canal.Writer.TryWrite(registro);
            }
        }
    }

    /// <summary>
    /// Entrega o histórico e depois os registros novos, até o cancelamento.
    /// </summary>
    /// <param name="cancellationToken">Cancelado quando o painel desconecta ou o app encerra.</param>
    /// <returns>Sequência assíncrona de registros.</returns>
    public async IAsyncEnumerable<RegistroDeLog> Acompanhar([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var canal = Channel.CreateBounded<RegistroDeLog>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        RegistroDeLog[] historico;
        lock (_trava)
        {
            // Copia o histórico e se inscreve sob a mesma trava: nenhum registro cai entre os dois.
            historico = [.. _historico];
            _assinantes.Add(canal);
        }

        try
        {
            foreach (var registro in historico)
            {
                yield return registro;
            }
            await foreach (var registro in canal.Reader.ReadAllAsync(cancellationToken))
            {
                yield return registro;
            }
        }
        finally
        {
            lock (_trava)
            {
                _assinantes.Remove(canal);
            }
        }
    }
}
