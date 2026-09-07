using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TurmaAmpliadaFaculdadeCidadeSemestreStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "turmas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Faculdade",
                table: "turmas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PrevisaoFormatura",
                table: "turmas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Semestre",
                table: "turmas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "turmas",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_turmas_Status",
                table: "turmas",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_turmas_Status",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "Faculdade",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "PrevisaoFormatura",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "Semestre",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "turmas");
        }
    }
}
