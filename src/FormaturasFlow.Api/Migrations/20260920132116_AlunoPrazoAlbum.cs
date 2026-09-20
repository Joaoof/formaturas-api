using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlunoPrazoAlbum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrazoAprovacaoAlbum",
                table: "alunos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VencimentoAprovacaoAlbum",
                table: "alunos",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrazoAprovacaoAlbum",
                table: "alunos");

            migrationBuilder.DropColumn(
                name: "VencimentoAprovacaoAlbum",
                table: "alunos");
        }
    }
}
