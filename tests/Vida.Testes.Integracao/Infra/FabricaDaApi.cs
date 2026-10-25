using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Vida.Producer.Outbox;

namespace Vida.Testes.Integracao.Infra;

/// <summary>
/// Sobe o Producer em memória contra um SQL Server descartável (Testcontainers). O Service Bus não é usado:
/// o despachante é tirado do host, e os testes do outbox o executam à mão com um sender falso.
/// </summary>
public sealed class FabricaDaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Chegam ao Program.cs como linha de comando, que vale mais que os arquivos *.local.json da máquina:
        // sem isso, o teste usaria o banco de desenvolvimento e mandaria telemetria para o App Insights.
        builder.UseSetting("ConnectionStrings:Propostas", _sql.GetConnectionString());
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "teste.servicebus.windows.net");
        builder.UseSetting("ApplicationInsights:ConnectionString", "");
        builder.UseEnvironment("Testes");

        builder.ConfigureServices(servicos =>
        {
            // O polling do despachante concorreria com as asserções sobre a tabela do outbox.
            var despachante = servicos.Single(s =>
                s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DespachanteDoOutbox));
            servicos.Remove(despachante);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
