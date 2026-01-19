using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTablesForConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IndexPriceSnapshots",
                table: "IndexPriceSnapshots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CapeDataSnapshots",
                table: "CapeDataSnapshots");

            migrationBuilder.RenameTable(
                name: "IndexPriceSnapshots",
                newName: "index_price_snapshots");

            migrationBuilder.RenameTable(
                name: "CapeDataSnapshots",
                newName: "cape_data_snapshots");

            migrationBuilder.RenameIndex(
                name: "IX_IndexPriceSnapshots_MarketKey_YearMonth",
                table: "index_price_snapshots",
                newName: "IX_index_price_snapshots_MarketKey_YearMonth");

            migrationBuilder.RenameIndex(
                name: "IX_CapeDataSnapshots_DataDate",
                table: "cape_data_snapshots",
                newName: "IX_cape_data_snapshots_DataDate");

            migrationBuilder.AddPrimaryKey(
                name: "PK_index_price_snapshots",
                table: "index_price_snapshots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cape_data_snapshots",
                table: "cape_data_snapshots",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_index_price_snapshots",
                table: "index_price_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_cape_data_snapshots",
                table: "cape_data_snapshots");

            migrationBuilder.RenameTable(
                name: "index_price_snapshots",
                newName: "IndexPriceSnapshots");

            migrationBuilder.RenameTable(
                name: "cape_data_snapshots",
                newName: "CapeDataSnapshots");

            migrationBuilder.RenameIndex(
                name: "IX_index_price_snapshots_MarketKey_YearMonth",
                table: "IndexPriceSnapshots",
                newName: "IX_IndexPriceSnapshots_MarketKey_YearMonth");

            migrationBuilder.RenameIndex(
                name: "IX_cape_data_snapshots_DataDate",
                table: "CapeDataSnapshots",
                newName: "IX_CapeDataSnapshots_DataDate");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IndexPriceSnapshots",
                table: "IndexPriceSnapshots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CapeDataSnapshots",
                table: "CapeDataSnapshots",
                column: "Id");
        }
    }
}
