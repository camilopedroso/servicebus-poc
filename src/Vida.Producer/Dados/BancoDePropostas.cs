using Microsoft.EntityFrameworkCore;
using Vida.Producer.Outbox;

namespace Vida.Producer.Dados;

/// <summary>
/// Banco da API de propostas (SQL Server local, em Docker). Registradas e OutboxMensagens ficam no mesmo banco
/// de propósito: é o que permite gravar as duas numa única transação local, sem 2PC com o Service Bus.
/// </summary>
/// <param name="options">Opções do EF Core (connection string vem de src/sqlserver.local.json).</param>
public sealed class BancoDePropostas(DbContextOptions<BancoDePropostas> options) : DbContext(options)
{
    public DbSet<PropostaRegistrada> Propostas => Set<PropostaRegistrada>();

    public DbSet<MensagemDeSaida> OutboxMensagens => Set<MensagemDeSaida>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Schema com nome de negócio no lugar do dbo: [VidaPropostas].[Propostas].[OutboxMensagens].
        // Em SQL Server o schema é namespace e fronteira de permissão (GRANT ... ON SCHEMA::Propostas).
        modelBuilder.HasDefaultSchema("Propostas");

        modelBuilder.Entity<PropostaRegistrada>(proposta =>
        {
            proposta.ToTable("Registradas");
            proposta.HasKey(p => p.PropostaId);
            proposta.Property(p => p.ClienteId).HasMaxLength(20);
            proposta.Property(p => p.Plano).HasConversion<string>().HasMaxLength(20);
            proposta.Property(p => p.CapitalSegurado).HasPrecision(18, 2);
            proposta.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
            proposta.Property(p => p.ChaveDeIdempotencia).HasMaxLength(100);
            // Único só entre quem mandou a chave (filtro): várias propostas sem chave continuam permitidas.
            proposta.HasIndex(p => p.ChaveDeIdempotencia).IsUnique().HasFilter("[ChaveDeIdempotencia] IS NOT NULL");
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
