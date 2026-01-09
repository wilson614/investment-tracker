using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndexPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MarketKey = table.Column<string>(type: "TEXT", nullable: false),
                    YearMonth = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexPriceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_splits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Market = table.Column<int>(type: "INTEGER", nullable: false),
                    SplitDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SplitRatio = table.Column<decimal>(type: "TEXT", precision: 10, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_splits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_Symbol_Market",
                table: "stock_splits",
                columns: new[] { "Symbol", "Market" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSplit_Symbol_Market_SplitDate",
                table: "stock_splits",
                columns: new[] { "Symbol", "Market", "SplitDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexPriceSnapshots");

            migrationBuilder.DropTable(
                name: "stock_splits");
        }
    }
}
