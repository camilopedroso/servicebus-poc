using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vida.Producer.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMensagens",
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
                name: "Propostas",
                columns: table => new
                {
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Idade = table.Column<int>(type: "int", nullable: false),
                    CapitalSegurado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Plano = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecebidaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EnviadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ChaveDeIdempotencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Propostas", x => x.PropostaId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMensagens_Pendentes",
                table: "OutboxMensagens",
                column: "Id",
                filter: "[PublicadaEm] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Propostas_ChaveDeIdempotencia",
                table: "Propostas",
                column: "ChaveDeIdempotencia",
                unique: true,
                filter: "[ChaveDeIdempotencia] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMensagens");

            migrationBuilder.DropTable(
                name: "Propostas");
        }
    }
}
