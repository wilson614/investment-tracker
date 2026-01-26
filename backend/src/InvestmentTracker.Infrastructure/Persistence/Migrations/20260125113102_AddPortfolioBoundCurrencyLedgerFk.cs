using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioBoundCurrencyLedgerFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId");

            migrationBuilder.AddForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId",
                principalTable: "currency_ledgers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios");

            migrationBuilder.DropIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios");
        }
    }
}
