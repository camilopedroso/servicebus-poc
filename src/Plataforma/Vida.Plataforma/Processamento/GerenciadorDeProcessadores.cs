using Microsoft.Extensions.Hosting;

namespace Vida.Plataforma.Processamento;

/// <summary>
/// Dono dos ProcessadorControlavel do app: liga os de partida quando o app sobe, liga e desliga cada um
/// a pedido do ProcessadoresController e para todos no encerramento.
/// </summary>
/// <param name="processadores">Todos os registrados com AddProcessadorControlavel, na ordem de registro.</param>
public sealed class GerenciadorDeProcessadores(IEnumerable<ProcessadorControlavel> processadores) : BackgroundService
{
    private readonly ProcessadorControlavel[] _processadores = [.. processadores];

    // Serializa iniciar/parar: dois cliques seguidos no painel não disputam o mesmo processor.
    private readonly SemaphoreSlim _trava = new(1, 1);

    /// <summary>Processadores na ordem de registro.</summary>
    public IReadOnlyList<ProcessadorControlavel> Processadores => _processadores;

    /// <summary>
    /// Procura um processador pelo nome, em qualquer grafia ("Propostas", "COBRANCA").
    /// </summary>
    /// <param name="nome">Nome do processador.</param>
    /// <returns>O processador, ou null se não houver um com esse nome.</returns>
    public ProcessadorControlavel? Encontrar(string nome)
    {
        foreach (var processador in _processadores)
        {
            if (string.Equals(processador.Nome, nome, StringComparison.OrdinalIgnoreCase))
            {
                return processador;
            }
        }
        return null;
    }

    /// <summary>
    /// Liga um processador.
    /// </summary>
    /// <param name="processador">Processador a ligar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public Task IniciarAsync(ProcessadorControlavel processador, CancellationToken cancellationToken) =>
        ComTrava(() => processador.IniciarAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Desliga um processador.
    /// </summary>
    /// <param name="processador">Processador a desligar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public Task PararAsync(ProcessadorControlavel processador, CancellationToken cancellationToken) =>
        ComTrava(() => processador.PararAsync(cancellationToken), cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var processador in _processadores)
        {
            if (processador.LigarNaPartida)
            {
                await IniciarAsync(processador, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // Para de buscar e espera as mensagens em andamento. O Dispose fica com o contêiner de DI.
        foreach (var processador in _processadores)
        {
            await processador.PararAsync(cancellationToken);
        }
    }

    private async Task ComTrava(Func<Task> operacao, CancellationToken cancellationToken)
    {
        await _trava.WaitAsync(cancellationToken);
        try
        {
            await operacao();
        }
        finally
        {
            _trava.Release();
        }
    }
}
