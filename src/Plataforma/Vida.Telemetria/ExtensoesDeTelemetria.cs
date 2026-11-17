using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Vida.Telemetria;

/// <summary>
/// Liga o OpenTelemetry (Microsoft OpenTelemetry Distro) em todos os serviços: traces, métricas e logs.
/// Destinos, detectados pela configuração:
/// - Application Insights, se houver ApplicationInsights:ConnectionString (gravada por infra/up.ps1);
/// - qualquer backend OTLP, se houver OTEL_EXPORTER_OTLP_ENDPOINT (ex.: Aspire Dashboard local, iniciar.ps1 -ComAspire).
/// Sem nenhum dos dois, não liga nada: o app roda igual, sem exportar.
/// </summary>
public static class ExtensoesDeTelemetria
{
    /// <summary>
    /// Configura a telemetria do app.
    /// </summary>
    /// <param name="builder">Builder do app.</param>
    /// <param name="servico">Nome do serviço: vira o "cloud role name" no Application Map do App Insights.</param>
    /// <returns>O mesmo builder, para encadear.</returns>
    public static WebApplicationBuilder AddTelemetria(this WebApplicationBuilder builder, string servico)
    {
        // A instrumentação do SDK do Service Bus via ActivitySource ainda é experimental e vem desligada.
        // A distro liga a do Azure SDK; o switch deixa explícito (e garante) que os spans do Service Bus saem.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        var conexao = builder.Configuration["ApplicationInsights:ConnectionString"];
        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(conexao) && string.IsNullOrWhiteSpace(otlp))
        {
            return builder;
        }

        var openTelemetry = builder.Services.AddOpenTelemetry();
        openTelemetry.ConfigureResource(recurso => recurso.AddService(servico));
        openTelemetry.WithTracing(traces => traces.AddSource(FonteDeAtividades.Nome));
        openTelemetry.WithMetrics(metricas => metricas.AddMeter(MetricasDeNegocio.Nome));
        openTelemetry.UseMicrosoftOpenTelemetry(opcoes =>
        {
            // Explícito: com a connection string definida em código, a autodetecção deixava o OTLP de fora.
            var paraAzureMonitor = string.IsNullOrWhiteSpace(conexao) ? ExportTarget.None : ExportTarget.AzureMonitor;
            var paraOtlp = string.IsNullOrWhiteSpace(otlp) ? ExportTarget.None : ExportTarget.Otlp;
            opcoes.Exporters = paraAzureMonitor | paraOtlp;
            if (!string.IsNullOrWhiteSpace(conexao))
            {
                opcoes.AzureMonitor.ConnectionString = conexao;
                // O App Insights tem DisableLocalAuth: sem token Entra ID, a ingestão é recusada.
                // Localmente é o az login; no Azure seria a managed identity do app.
                opcoes.AzureMonitor.Credential = new DefaultAzureCredential();
                // Na POC, todo trace interessa: sem amostragem (o padrão limita a 5 traces/s).
                opcoes.AzureMonitor.TracesPerSecond = null;
                opcoes.AzureMonitor.SamplingRatio = 1.0F;
            }
        });

        // Os logs (ILogger) vão junto, já com TraceId/SpanId: no App Insights, a linha de log aparece dentro do trace.
        builder.Logging.AddFilter<OpenTelemetry.Logs.OpenTelemetryLoggerProvider>("Azure", LogLevel.Warning);
        return builder;
    }
}
