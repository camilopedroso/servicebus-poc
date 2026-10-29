using System.Diagnostics.Metrics;

namespace Vida.Telemetria;

/// <summary>
/// Métricas de negócio. No Application Insights viram customMetrics; dá para alertar em cima delas
/// (ex.: atraso do outbox acima de 30 s = Service Bus com problema).
/// </summary>
public static class MetricasDeNegocio
{
    /// <summary>Nome registrado no MeterProvider (ExtensoesDeTelemetria).</summary>
    public const string Nome = "Vida";

    private static readonly Meter Medidor = new(Nome);

    /// <summary>Propostas analisadas pela Subscrição, com a tag "resultado" (aprovada, recusada).</summary>
    public static readonly Counter<long> PropostasDecididas =
        Medidor.CreateCounter<long>("vida.propostas.decididas", description: "Propostas analisadas, por resultado.");

    /// <summary>Tempo entre gravar a mensagem no outbox e publicá-la no Service Bus.</summary>
    public static readonly Histogram<double> AtrasoDoOutbox =
        Medidor.CreateHistogram<double>("vida.outbox.atraso", unit: "ms", description: "Da gravação no outbox à publicação.");

    /// <summary>Endossos que chegaram antes da hora e foram adiados (deferral).</summary>
    public static readonly Counter<long> EndossosAdiados =
        Medidor.CreateCounter<long>("vida.endossos.adiados", description: "Endossos adiados por chegarem fora de ordem.");
}
