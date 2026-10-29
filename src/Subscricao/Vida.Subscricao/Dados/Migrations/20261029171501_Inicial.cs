using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vida.Subscricao.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Subscricao");

            migrationBuilder.CreateTable(
                name: "Apolices",
                schema: "Subscricao",
                columns: table => new
                {
                    ApoliceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Plano = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CapitalSegurado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PremioMensal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequerRevisao = table.Column<bool>(type: "bit", nullable: false),
                    EmitidaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apolices", x => x.ApoliceId);
                });

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: "Subscricao",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProcessadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMensagens",
                schema: "Subscricao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Destino = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Corpo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Propriedades = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublicadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UltimoErro = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMensagens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recusas",
                schema: "Subscricao",
                columns: table => new
                {
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecusadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recusas", x => x.PropostaId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apolices_PropostaId",
                schema: "Subscricao",
                table: "Apolices",
                column: "PropostaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMensagens_Pendentes",
                schema: "Subscricao",
                table: "OutboxMensagens",
                column: "Id",
                filter: "[PublicadaEm] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Apolices",
                schema: "Subscricao");

            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "Subscricao");

            migrationBuilder.DropTable(
                name: "OutboxMensagens",
                schema: "Subscricao");

            migrationBuilder.DropTable(
                name: "Recusas",
                schema: "Subscricao");
        }
    }
}
