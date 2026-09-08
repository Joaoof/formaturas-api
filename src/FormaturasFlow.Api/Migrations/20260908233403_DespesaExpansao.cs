using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class DespesaExpansao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormaPagamento",
                table: "despesas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "despesas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TurmaId",
                table: "despesas",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormaPagamento",
                table: "despesas");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "despesas");

            migrationBuilder.DropColumn(
                name: "TurmaId",
                table: "despesas");
        }
    }
}
