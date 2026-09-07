using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormaturasFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCobrancas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobrancas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: false),
                    ClienteNome = table.Column<string>(type: "text", nullable: false),
                    ClienteCpf = table.Column<string>(type: "text", nullable: true),
                    ClienteEmail = table.Column<string>(type: "text", nullable: true),
                    ClienteWhatsapp = table.Column<string>(type: "text", nullable: true),
                    ClienteTelefone = table.Column<string>(type: "text", nullable: true),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ValorPago = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    DataPagamento = table.Column<DateOnly>(type: "date", nullable: true),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    TipoPagamento = table.Column<string>(type: "text", nullable: false),
                    NumParcelasCartao = table.Column<int>(type: "integer", nullable: true),
                    TipoEvento = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PspProvider = table.Column<string>(type: "text", nullable: true),
                    PspCustomerId = table.Column<string>(type: "text", nullable: true),
                    PspChargeId = table.Column<string>(type: "text", nullable: true),
                    PspStatus = table.Column<string>(type: "text", nullable: true),
                    BoletoUrl = table.Column<string>(type: "text", nullable: true),
                    BoletoLinhaDigitavel = table.Column<string>(type: "text", nullable: true),
                    BoletoCodigoBarras = table.Column<string>(type: "text", nullable: true),
                    PixCopiaCola = table.Column<string>(type: "text", nullable: true),
                    PixQrCodeUrl = table.Column<string>(type: "text", nullable: true),
                    LinkPagamento = table.Column<string>(type: "text", nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cobrancas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cobrancas_ExternalReference",
                table: "cobrancas",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_cobrancas_PspChargeId",
                table: "cobrancas",
                column: "PspChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_cobrancas_Status",
                table: "cobrancas",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobrancas");
        }
    }
}
