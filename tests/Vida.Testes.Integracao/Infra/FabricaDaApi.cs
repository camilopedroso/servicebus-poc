using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Vida.Propostas.Desfechos;
using Vida.Propostas.Outbox;

namespace Vida.Testes.Integracao.Infra;

/// <summary>
/// Sobe o serviço de Propostas em memória contra um SQL Server descartável (Testcontainers). O Service Bus não é usado:
/// o despachante é tirado do host, e os testes do outbox o executam à mão com um sender falso.
/// <para>
/// Três peças:
/// <list type="bullet">
/// <item><see cref="WebApplicationFactory{TEntryPoint}"/> (Microsoft.AspNetCore.Mvc.Testing): roda o Program.cs do
/// serviço de Propostas de verdade, com DI, middlewares e controllers, sobre um TestServer em memória. O CreateClient() devolve
/// um HttpClient que fala com ele sem abrir porta.</item>
/// <item><see cref="MsSqlContainer"/> (Testcontainers.MsSql): um SQL Server real em Docker, criado só para esta
/// execução. Na partida, o serviço aplica as migrations nele (MigracaoDoBanco), então o banco começa vazio e com
/// exatamente o schema que as migrations produzem.</item>
/// <item><see cref="IAsyncLifetime"/> (xUnit): sobe o container antes do primeiro teste e o destrói no fim. Como a
/// fábrica é fixture da coleção <see cref="ColecaoDoBanco"/>, isso acontece uma vez para todos os testes.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FabricaDaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Mesma imagem do infra/sql-up.ps1: o teste roda na mesma engine e versão do banco de desenvolvimento.
    // O Testcontainers mapeia a 1433 do container para uma porta livre aleatória, então não conflita com o
    // vida-sql (14330), e espera o SQL Server aceitar conexão antes de liberar o StartAsync.
    private readonly MsSqlContainer _sql;

    public FabricaDaApi()
    {
        var construtor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest");
        _sql = construtor.Build();
    }

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Chegam ao Program.cs como linha de comando, que vale mais que os arquivos *.local.json da máquina:
        // sem isso, o teste usaria o banco de desenvolvimento e mandaria telemetria para o App Insights.
        // A connection string já vem com host, porta sorteada, usuário e senha do container.
        var conexao = _sql.GetConnectionString();
        builder.UseSetting("ConnectionStrings:Propostas", conexao);
        // Namespace fictício: o ServiceBusClient de Propostas só conecta no primeiro envio, e nenhum teste daqui
        // envia por ele (a API grava no outbox; o despachante dos testes usa o ServiceBusFalso).
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "teste.servicebus.windows.net");
        builder.UseSetting("ApplicationInsights:ConnectionString", "");
        builder.UseEnvironment("Testes");

        builder.ConfigureServices(servicos =>
        {
            // O polling do despachante concorreria com as asserções sobre a tabela do outbox.
            var despachante = servicos.Single(s =>
                s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DespachanteDoOutbox));
            servicos.Remove(despachante);
            // O processor de desfechos tentaria conectar no namespace fictício; os testes aplicam os eventos
            // direto pelo RegistroDeDesfechos.
            var desfechos = servicos.Single(s =>
                s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(ProcessadorDeDesfechos));
            servicos.Remove(desfechos);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        // Primeiro o host (fecha as conexões do EF), depois o container. Se o processo morrer antes de chegar
        // aqui, o Ryuk (container auxiliar do Testcontainers) remove o SQL Server sozinho.
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
