using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlunoContratoAmpliados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutorizaImagem",
                table: "contratos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Desconto",
                table: "contratos",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DiaVencimento",
                table: "contratos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AlbumLiberado",
                table: "alunos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DataNascimento",
                table: "alunos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FotosLiberadas",
                table: "alunos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LinkAprovacaoAlbum",
                table: "alunos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkFotosSelecionadas",
                table: "alunos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoInativacao",
                table: "alunos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrazoFotosSelecionadas",
                table: "alunos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "alunos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "VencimentoFotosSelecionadas",
                table: "alunos",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_alunos_Status",
                table: "alunos",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alunos_Status",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "AutorizaImagem",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "Desconto",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "DiaVencimento",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AlbumLiberado",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "DataNascimento",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "FotosLiberadas",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "LinkAprovacaoAlbum",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "LinkFotosSelecionadas",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "MotivoInativacao",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "PrazoFotosSelecionadas",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "VencimentoFotosSelecionadas",
                table: "alunos");
        }
    }
}
