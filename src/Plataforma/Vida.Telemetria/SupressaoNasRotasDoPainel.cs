using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;

namespace Vida.Telemetria;

/// <summary>
/// Roda as rotas que o painel chama o tempo todo (SSE de logs, polling das assinaturas) dentro de
/// <see cref="SuppressInstrumentationScope"/>: nenhum span nasce enquanto a requisição é atendida.
/// O filtro da instrumentação do ASP.NET Core só descarta o span da requisição. Os filhos (ex.: as chamadas do
/// administration client no GET /assinaturas) continuavam sendo exportados, órfãos, porque o sampler da distro
/// decide pelo TraceId e não herda a decisão do pai.
/// Registrado como <see cref="IStartupFilter"/> para entrar no começo do pipeline sem mexer no Program.cs dos apps.
/// </summary>
internal sealed class SupressaoNasRotasDoPainel : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) => app =>
    {
        app.Use(async (contexto, seguinte) =>
        {
            if (!RotasDoPainel.Contem(contexto.Request))
            {
                await seguinte(contexto);
                return;
            }

            using var supressao = SuppressInstrumentationScope.Begin();
            await seguinte(contexto);
        });
        proximo(app);
    };
}
