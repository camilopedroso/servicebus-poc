using Microsoft.EntityFrameworkCore;
using OpenTelemetry;

namespace Vida.Subscricao.Dados;

/// <summary>
/// Aplica as migrations do EF Core na partida (cria o banco VidaSubscricao na primeira vez).
/// Bom para a POC; em produção a migration roda no pipeline (script ou bundle), não no app.
/// Não derruba o app se o SQL Server estiver fora: os endossos e as demonstrações continuam funcionando.
/// </summary>
public sealed class MigracaoDoBanco(IServiceScopeFactory escopos, ILogger<MigracaoDoBanco> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // O container pode estar acabando de subir: algumas tentativas antes de desistir.
        for (var tentativa = 1; tentativa <= 10; tentativa++)
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                var banco = escopo.ServiceProvider.GetRequiredService<BancoDaSubscricao>();
                // As consultas da migração (histórico, CREATE TABLE) não são trabalho do app: fora do trace.
                using (SuppressInstrumentationScope.Begin())
                {
                    await banco.Database.MigrateAsync(cancellationToken);
                }
                logger.LogInformation("Banco VidaSubscricao pronto (migrations aplicadas).");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && tentativa < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    "SQL Server indisponível ({Erro}). A análise de propostas não vai funcionar; rode infra\\sql-up.ps1.",
                    ex.Message);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
