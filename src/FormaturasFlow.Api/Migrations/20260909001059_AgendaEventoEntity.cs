using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AgendaEventoEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agenda_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    EmpresaTipo = table.Column<string>(type: "text", nullable: false),
                    EmpresaNome = table.Column<string>(type: "text", nullable: false),
                    LocalEvento = table.Column<string>(type: "text", nullable: true),
                    Cidade = table.Column<string>(type: "text", nullable: true),
                    Fotografo = table.Column<string>(type: "text", nullable: true),
                    DataEvento = table.Column<DateOnly>(type: "date", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agenda_eventos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agenda_eventos_DataEvento",
                table: "agenda_eventos",
                column: "DataEvento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_eventos");
        }
    }
}
