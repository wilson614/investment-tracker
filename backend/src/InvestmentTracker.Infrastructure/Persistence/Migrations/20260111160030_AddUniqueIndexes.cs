using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IndexPriceSnapshots_MarketKey_YearMonth",
                table: "IndexPriceSnapshots",
                columns: new[] { "MarketKey", "YearMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndexPriceSnapshots_MarketKey_YearMonth",
                table: "IndexPriceSnapshots");
        }
    }
}
