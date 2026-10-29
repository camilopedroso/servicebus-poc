using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Vida.Plataforma.Processamento;

/// <summary>
/// Processor do Service Bus que pode ser ligado e desligado com o app no ar (POST /processadores/{nome}/parar).
/// Parado, o processo continua rodando e as mensagens novas ficam guardadas na entidade até ele ser religado:
/// é o que mostra que quem publica não depende de quem consome estar de pé.
/// Aceita ServiceBusProcessor e ServiceBusSessionProcessor, que têm os mesmos métodos mas nenhuma interface em comum.
/// </summary>
public sealed class ProcessadorControlavel : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _iniciar;
    private readonly Func<CancellationToken, Task> _parar;
    private readonly Func<bool> _estaProcessando;
    private readonly Func<ValueTask> _liberar;
    private readonly ILogger _logger;

    private const string SeparadorDeAssinatura = "/Subscriptions/";

    // Identifica a instância nos logs para comparar quando houver mais de um consumer (competing consumers).
    private readonly string _instancia = $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>
    /// Embrulha um processor de fila ou de assinatura.
    /// </summary>
    /// <param name="nome">Nome usado na rota e no painel (ex.: "propostas", "cobranca").</param>
    /// <param name="processor">Processor já com os handlers de mensagem e de erro.</param>
    /// <param name="logger">Logger de quem criou o processor: os logs de ligar/desligar saem com a categoria dele.</param>
    /// <param name="ligarNaPartida">Se o GerenciadorDeProcessadores liga este processor quando o app sobe.</param>
    public ProcessadorControlavel(string nome, ServiceBusProcessor processor, ILogger logger, bool ligarNaPartida = true)
    {
        Nome = nome;
        Entidade = processor.EntityPath;
        // Processor de assinatura tem EntityPath "topico/Subscriptions/assinatura"; o de fila, só o nome da fila.
        var partes = processor.EntityPath.Split(SeparadorDeAssinatura);
        if (partes.Length == 2)
        {
            Topico = partes[0];
            Assinatura = partes[1];
        }
        LigarNaPartida = ligarNaPartida;
        _iniciar = processor.StartProcessingAsync;
        _parar = processor.StopProcessingAsync;
        _estaProcessando = () => processor.IsProcessing;
        _liberar = processor.DisposeAsync;
        _logger = logger;
    }

    /// <summary>
    /// Embrulha um processor de fila com sessions.
    /// </summary>
    /// <param name="nome">Nome usado na rota e no painel (ex.: "endossos").</param>
    /// <param name="processor">Session processor já com os handlers.</param>
    /// <param name="logger">Logger de quem criou o processor.</param>
    /// <param name="ligarNaPartida">Se o GerenciadorDeProcessadores liga este processor quando o app sobe.</param>
    public ProcessadorControlavel(string nome, ServiceBusSessionProcessor processor, ILogger logger, bool ligarNaPartida = true)
    {
        Nome = nome;
        Entidade = processor.EntityPath;
        LigarNaPartida = ligarNaPartida;
        _iniciar = processor.StartProcessingAsync;
        _parar = processor.StopProcessingAsync;
        _estaProcessando = () => processor.IsProcessing;
        _liberar = processor.DisposeAsync;
        _logger = logger;
    }

    /// <summary>Nome usado na rota e no painel.</summary>
    public string Nome { get; }

    /// <summary>Fila ou assinatura lida (para assinatura: "topico/Subscriptions/assinatura").</summary>
    public string Entidade { get; }

    /// <summary>Tópico, quando o processor lê uma assinatura; null para fila.</summary>
    public string? Topico { get; }

    /// <summary>Assinatura, quando o processor lê uma; null para fila.</summary>
    public string? Assinatura { get; }

    /// <summary>Se o processor é ligado quando o app sobe.</summary>
    public bool LigarNaPartida { get; }

    /// <summary>True enquanto o processor estiver buscando mensagens.</summary>
    public bool Ativo => _estaProcessando();

    /// <summary>
    /// Começa a receber mensagens. Não faz nada se já estiver ativo.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public async Task IniciarAsync(CancellationToken cancellationToken)
    {
        if (Ativo)
        {
            return;
        }

        // O mesmo processor pode ser parado e iniciado de novo quantas vezes for preciso.
        await _iniciar(cancellationToken);
        _logger.LogInformation("[{Processador}] Ouvindo '{Entidade}' (instância {Instancia})...", Nome, Entidade, _instancia);
    }

    /// <summary>
    /// Para de buscar mensagens e espera as que estão em andamento terminarem (o settlement delas acontece normalmente).
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public async Task PararAsync(CancellationToken cancellationToken)
    {
        if (!Ativo)
        {
            return;
        }

        await _parar(cancellationToken);
        _logger.LogInformation(
            "[{Processador}] Parado. O processo continua no ar; as novas mensagens ficam guardadas em '{Entidade}' até ele ser religado.",
            Nome,
            Entidade);
    }

    /// <summary>
    /// Libera o processor (e o link AMQP dele). Chamado pelo contêiner de DI no encerramento.
    /// </summary>
    /// <returns>Tarefa representando a operação assíncrona.</returns>
    public ValueTask DisposeAsync() => _liberar();
}
