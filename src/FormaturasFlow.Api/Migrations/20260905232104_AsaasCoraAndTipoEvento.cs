using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AsaasCoraAndTipoEvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DataEvento",
                table: "turmas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoEvento",
                table: "turmas",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AsaasCustomerId",
                table: "alunos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_turmas_TipoEvento",
                table: "turmas",
                column: "TipoEvento");

            migrationBuilder.CreateIndex(
                name: "IX_alunos_AsaasCustomerId",
                table: "alunos",
                column: "AsaasCustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_turmas_TipoEvento",
                table: "turmas");

            migrationBuilder.DropIndex(
                name: "IX_alunos_AsaasCustomerId",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "DataEvento",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "TipoEvento",
                table: "turmas");

            migrationBuilder.DropColumn(
                name: "AsaasCustomerId",
                table: "alunos");
        }
    }
}
