using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vida.Subscricao.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Endossos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                schema: "Subscricao",
                table: "OutboxMensagens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersaoDosEndossos",
                schema: "Subscricao",
                table: "Apolices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Endossos",
                schema: "Subscricao",
                columns: table => new
                {
                    ApoliceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SolicitadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AplicadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ChaveDeIdempotencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Endossos", x => new { x.ApoliceId, x.Versao });
                    table.ForeignKey(
                        name: "FK_Endossos_Apolices_ApoliceId",
                        column: x => x.ApoliceId,
                        principalSchema: "Subscricao",
                        principalTable: "Apolices",
                        principalColumn: "ApoliceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Endossos_ApoliceId_ChaveDeIdempotencia",
                schema: "Subscricao",
                table: "Endossos",
                columns: new[] { "ApoliceId", "ChaveDeIdempotencia" },
                unique: true,
                filter: "[ChaveDeIdempotencia] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Endossos",
                schema: "Subscricao");

            migrationBuilder.DropColumn(
                name: "SessionId",
                schema: "Subscricao",
                table: "OutboxMensagens");

            migrationBuilder.DropColumn(
                name: "VersaoDosEndossos",
                schema: "Subscricao",
                table: "Apolices");
        }
    }
}
