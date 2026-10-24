using Azure.Messaging.ServiceBus;
using Vida.Mensageria.Topologia;

namespace Vida.Assinantes.Processamento;

/// <summary>
/// Mantém um ProcessadorDeAssinatura para cada assinatura do tópico e permite ligar e desligar cada um
/// enquanto o app roda. Todos compartilham o mesmo ServiceBusClient (uma conexão AMQP).
/// </summary>
public sealed class GerenciadorDeAssinaturas : BackgroundService
{
    private readonly Dictionary<string, ProcessadorDeAssinatura> _processadores;
    private readonly string[] _iniciais;

    // Serializa iniciar/parar: dois cliques seguidos no painel não disputam o mesmo processor.
    private readonly SemaphoreSlim _trava = new(1, 1);

    /// <summary>
    /// Cria o gerenciador.
    /// </summary>
    /// <param name="iniciais">Assinaturas ligadas na partida (argumentos da linha de comando, ou todas).</param>
    /// <param name="client">ServiceBusClient compartilhado.</param>
    /// <param name="logger">Logger dos processadores.</param>
    public GerenciadorDeAssinaturas(string[] iniciais, ServiceBusClient client, ILogger<ProcessadorDeAssinatura> logger)
    {
        _iniciais = iniciais;
        _processadores = Assinaturas.Todas.ToDictionary(
            nome => nome,
            nome => new ProcessadorDeAssinatura(nome, client, logger));
    }

    /// <summary>Processadores na ordem de Assinaturas.Todas.</summary>
    public IEnumerable<ProcessadorDeAssinatura> Processadores => Assinaturas.Todas.Select(nome => _processadores[nome]);

    /// <summary>True se a assinatura existe na topologia.</summary>
    public bool Existe(string nome) => _processadores.ContainsKey(nome);

    /// <summary>
    /// Liga uma assinatura.
    /// </summary>
    /// <param name="nome">Nome da assinatura.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public Task IniciarAsync(string nome, CancellationToken cancellationToken) =>
        ComTrava(() => _processadores[nome].IniciarAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Desliga uma assinatura.
    /// </summary>
    /// <param name="nome">Nome da assinatura.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public Task PararAsync(string nome, CancellationToken cancellationToken) =>
        ComTrava(() => _processadores[nome].PararAsync(cancellationToken), cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var nome in _iniciais)
        {
            await IniciarAsync(nome, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var processador in _processadores.Values)
        {
            await processador.PararAsync(cancellationToken);
            await processador.DisposeAsync();
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
