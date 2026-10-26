using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vida.Producer.Dados.Migrations
{
    /// <inheritdoc />
    public partial class SchemaPropostas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Propostas",
                table: "Propostas");

            migrationBuilder.EnsureSchema(
                name: "Propostas");

            migrationBuilder.RenameTable(
                name: "OutboxMensagens",
                newName: "OutboxMensagens",
                newSchema: "Propostas");

            migrationBuilder.RenameTable(
                name: "Propostas",
                newName: "Registradas",
                newSchema: "Propostas");

            migrationBuilder.RenameIndex(
                name: "IX_Propostas_ChaveDeIdempotencia",
                schema: "Propostas",
                table: "Registradas",
                newName: "IX_Registradas_ChaveDeIdempotencia");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Registradas",
                schema: "Propostas",
                table: "Registradas",
                column: "PropostaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Registradas",
                schema: "Propostas",
                table: "Registradas");

            migrationBuilder.RenameTable(
                name: "OutboxMensagens",
                schema: "Propostas",
                newName: "OutboxMensagens");

            migrationBuilder.RenameTable(
                name: "Registradas",
                schema: "Propostas",
                newName: "Propostas");

            migrationBuilder.RenameIndex(
                name: "IX_Registradas_ChaveDeIdempotencia",
                table: "Propostas",
                newName: "IX_Propostas_ChaveDeIdempotencia");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Propostas",
                table: "Propostas",
                column: "PropostaId");
        }
    }
}
