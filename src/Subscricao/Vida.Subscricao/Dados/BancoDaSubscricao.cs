using Microsoft.EntityFrameworkCore;
using Vida.Subscricao.Inbox;
using Vida.Subscricao.Outbox;

namespace Vida.Subscricao.Dados;

/// <summary>
/// Banco da Subscrição (VidaSubscricao, no mesmo SQL Server local, mas outro banco: cada serviço lê só o seu).
/// Apólices, recusas, inbox e outbox no mesmo banco: é o que permite gravar tudo numa transação local.
/// A transação do Service Bus (fase 2) não serve mais aqui: ela não inclui o SQL Server.
/// </summary>
/// <param name="options">Opções do EF Core (connection string vem de src/sqlserver.local.json).</param>
public sealed class BancoDaSubscricao(DbContextOptions<BancoDaSubscricao> options) : DbContext(options)
{
    public DbSet<ApoliceRegistrada> Apolices => Set<ApoliceRegistrada>();

    public DbSet<RecusaRegistrada> Recusas => Set<RecusaRegistrada>();

    public DbSet<MensagemRecebida> Inbox => Set<MensagemRecebida>();

    public DbSet<MensagemDeSaida> OutboxMensagens => Set<MensagemDeSaida>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("Subscricao");

        modelBuilder.Entity<ApoliceRegistrada>(apolice =>
        {
            apolice.ToTable("Apolices");
            apolice.HasKey(a => a.ApoliceId);
            apolice.Property(a => a.ClienteId).HasMaxLength(20);
            apolice.Property(a => a.Plano).HasConversion<string>().HasMaxLength(20);
            apolice.Property(a => a.CapitalSegurado).HasPrecision(18, 2);
            apolice.Property(a => a.PremioMensal).HasPrecision(18, 2);
            apolice.HasIndex(a => a.PropostaId).IsUnique();
        });

        modelBuilder.Entity<RecusaRegistrada>(recusa =>
        {
            recusa.ToTable("Recusas");
            recusa.HasKey(r => r.PropostaId);
            recusa.Property(r => r.ClienteId).HasMaxLength(20);
            recusa.Property(r => r.Motivo).HasMaxLength(500);
        });

        modelBuilder.Entity<MensagemRecebida>(mensagem =>
        {
            mensagem.ToTable("Inbox");
            mensagem.HasKey(m => m.MessageId);
            mensagem.Property(m => m.MessageId).HasMaxLength(128);
        });

        modelBuilder.Entity<MensagemDeSaida>(mensagem =>
        {
            mensagem.ToTable("OutboxMensagens");
            mensagem.HasKey(m => m.Id);
            mensagem.Property(m => m.Destino).HasMaxLength(260);
            mensagem.Property(m => m.MessageId).HasMaxLength(128);
            mensagem.Property(m => m.Subject).HasMaxLength(100);
            mensagem.Property(m => m.CorrelationId).HasMaxLength(128);
            mensagem.Property(m => m.ContentType).HasMaxLength(100);
            mensagem.Property(m => m.UltimoErro).HasMaxLength(1000);
            // O despachante sempre procura "pendentes, em ordem": índice filtrado só com elas fica pequeno.
            mensagem.HasIndex(m => m.Id).HasFilter("[PublicadaEm] IS NULL").HasDatabaseName("IX_OutboxMensagens_Pendentes");
        });
    }
}
