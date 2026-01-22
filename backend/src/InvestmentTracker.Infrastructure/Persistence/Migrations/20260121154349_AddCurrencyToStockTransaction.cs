using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyToStockTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Currency column with default USD (2)
            migrationBuilder.AddColumn<int>(
                name: "Currency",
                table: "stock_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 2); // USD

            // Auto-populate based on Market:
            // Market = 1 (TW) -> Currency = 1 (TWD)
            // Market = 2,3,4 (US/UK/EU) -> Currency = 2 (USD)
            migrationBuilder.Sql(@"
                UPDATE stock_transactions
                SET ""Currency"" = CASE
                    WHEN ""Market"" = 1 THEN 1  -- TW -> TWD
                    ELSE 2                       -- Others -> USD
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "stock_transactions");
        }
    }
}
