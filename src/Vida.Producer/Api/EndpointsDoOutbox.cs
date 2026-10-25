using Microsoft.EntityFrameworkCore;
using Vida.Producer.Contratos;
using Vida.Producer.Dados;
using Vida.Producer.Outbox;

namespace Vida.Producer.Api;

/// <summary>
/// Operação do outbox (fase 4): ver pendentes e simular o Service Bus fora do ar.
/// </summary>
public static class EndpointsDoOutbox
{
    /// <summary>
    /// Mapeia GET /outbox e POST /outbox/simulacao.
    /// </summary>
    /// <param name="app">App onde os endpoints são mapeados.</param>
    /// <returns>O mesmo app, para encadear.</returns>
    public static IEndpointRouteBuilder MapOutbox(this IEndpointRouteBuilder app)
    {
        app.MapGet("/outbox", async (BancoDePropostas banco, SimulacaoDeIndisponibilidade simulacao, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var pendentes = await banco.OutboxMensagens.CountAsync(m => m.PublicadaEm == null, ct);
            var publicadas = await banco.OutboxMensagens.CountAsync(m => m.PublicadaEm != null, ct);
            loggers.CreateLogger("Vida.Producer.Api.Outbox").LogInformation(
                "Outbox: {Pendentes} pendente(s), {Publicadas} publicada(s). Service Bus {Situacao}.",
                pendentes, publicadas, simulacao.Ativa ? "indisponível (simulação)" : "disponível");
            return new EstadoDoOutbox(simulacao.Ativa, pendentes, publicadas);
        });

        app.MapPost("/outbox/simulacao", (SimulacaoDoOutbox corpo, SimulacaoDeIndisponibilidade simulacao, ILoggerFactory loggers) =>
        {
            simulacao.Ativa = corpo.Indisponivel;
            loggers.CreateLogger("Vida.Producer.Api.Outbox").LogWarning(corpo.Indisponivel
                ? "Simulação: Service Bus FORA DO AR para o despachante do outbox. A API continua aceitando propostas."
                : "Simulação: Service Bus de volta. O despachante publica o que acumulou.");
            return new { corpo.Indisponivel };
        });
        return app;
    }
}
