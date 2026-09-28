using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssinaturaContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssinadoEm",
                table: "contratos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinadoIp",
                table: "contratos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinadoUserAgent",
                table: "contratos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinanteCpf",
                table: "contratos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinanteNome",
                table: "contratos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinaturaHashDocumento",
                table: "contratos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinaturaImagem",
                table: "contratos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssinadoEm",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinadoIp",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinadoUserAgent",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinanteCpf",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinanteNome",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinaturaHashDocumento",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "AssinaturaImagem",
                table: "contratos");
        }
    }
}
