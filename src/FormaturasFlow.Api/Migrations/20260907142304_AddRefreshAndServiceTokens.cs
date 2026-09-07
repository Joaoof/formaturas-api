using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshAndServiceTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevogadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevogadoMotivo = table.Column<string>(type: "text", nullable: true),
                    SubstituidoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpCriacao = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "service_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CriadoPorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimoUsoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevogadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Escopo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_ExpiresAt",
                table: "refresh_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_CriadoPorUserId",
                table: "service_tokens",
                column: "CriadoPorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_ExpiresAt",
                table: "service_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_TokenHash",
                table: "service_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "service_tokens");
        }
    }
}
