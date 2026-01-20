#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NullableExchangeRateAndNewEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ExchangeRate",
                table: "stock_transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.CreateTable(
                name: "etf_classifications",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Market = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etf_classifications", x => new { x.Symbol, x.Market });
                });

            migrationBuilder.CreateTable(
                name: "euronext_quote_cache",
                columns: table => new
                {
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Mic = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MarketTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_euronext_quote_cache", x => new { x.Isin, x.Mic });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "etf_classifications");

            migrationBuilder.DropTable(
                name: "euronext_quote_cache");

            migrationBuilder.AlterColumn<decimal>(
                name: "ExchangeRate",
                table: "stock_transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);
        }
    }
}
