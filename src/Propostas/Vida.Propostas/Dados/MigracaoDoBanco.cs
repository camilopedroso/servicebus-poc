using Microsoft.EntityFrameworkCore;
using OpenTelemetry;

namespace Vida.Propostas.Dados;

/// <summary>
/// Aplica as migrations do EF Core na partida (cria o banco VidaPropostas na primeira vez).
/// Em produção a migration costuma rodar no pipeline (script ou bundle), não no app; aqui fica na partida para
/// simplificar o ambiente local. Se o banco não responder depois das tentativas, o host para (fail fast):
/// um serviço sem banco não tem o que fazer, e o orquestrador (App Service, Container Apps) reinicia.
/// </summary>
public sealed class MigracaoDoBanco(IServiceScopeFactory escopos, ILogger<MigracaoDoBanco> logger) : IHostedService
{
    private const int Tentativas = 10;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // O banco pode estar acabando de subir: algumas tentativas antes de desistir.
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                var banco = escopo.ServiceProvider.GetRequiredService<BancoDePropostas>();
                // As consultas da migração (histórico, CREATE TABLE) não são trabalho do app: fora do trace.
                using (SuppressInstrumentationScope.Begin())
                {
                    await banco.Database.MigrateAsync(cancellationToken);
                }
                logger.LogInformation("Banco VidaPropostas pronto (migrations aplicadas).");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && tentativa < Tentativas)
            {
                logger.LogWarning("Banco VidaPropostas indisponível (tentativa {Tentativa} de {Total}): {Erro}", tentativa, Tentativas, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
