using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vida.Propostas.Dados.Migrations
{
    /// <inheritdoc />
    public partial class DesfechoDaProposta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApoliceId",
                schema: "Propostas",
                table: "Registradas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecididaEm",
                schema: "Propostas",
                table: "Registradas",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDaRecusa",
                schema: "Propostas",
                table: "Registradas",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApoliceId",
                schema: "Propostas",
                table: "Registradas");

            migrationBuilder.DropColumn(
                name: "DecididaEm",
                schema: "Propostas",
                table: "Registradas");

            migrationBuilder.DropColumn(
                name: "MotivoDaRecusa",
                schema: "Propostas",
                table: "Registradas");
        }
    }
}
