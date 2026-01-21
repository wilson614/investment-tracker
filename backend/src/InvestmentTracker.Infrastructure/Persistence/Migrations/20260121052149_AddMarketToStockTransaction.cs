using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketToStockTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Market",
                table: "stock_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // Auto-populate Market based on ticker pattern (guessMarket logic)
            // TW (1): Starts with digit (e.g., 0050, 2330)
            // UK (3): Ends with .L (e.g., HSBA.L)
            // US (2): Default for everything else
            migrationBuilder.Sql(@"
                UPDATE stock_transactions SET ""Market"" =
                    CASE
                        WHEN ""Ticker"" ~ '^[0-9]' THEN 1
                        WHEN ""Ticker"" LIKE '%.L' THEN 3
                        ELSE 2
                    END;
            ");

            migrationBuilder.CreateTable(
                name: "user_benchmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Market = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_benchmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_benchmarks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserBenchmark_UserId",
                table: "user_benchmarks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBenchmark_UserId_Ticker_Market",
                table: "user_benchmarks",
                columns: new[] { "UserId", "Ticker", "Market" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_benchmarks");

            migrationBuilder.DropColumn(
                name: "Market",
                table: "stock_transactions");
        }
    }
}
