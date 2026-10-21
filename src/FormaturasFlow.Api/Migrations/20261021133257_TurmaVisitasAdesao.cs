using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TurmaVisitasAdesao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VisitasAdesao",
                table: "turmas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisitasAdesao",
                table: "turmas");
        }
    }
}
