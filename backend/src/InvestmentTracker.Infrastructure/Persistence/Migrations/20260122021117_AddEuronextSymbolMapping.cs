using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEuronextSymbolMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "euronext_symbol_mappings",
                columns: table => new
                {
                    ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    isin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mic = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_euronext_symbol_mappings", x => x.ticker);
                });

            migrationBuilder.CreateIndex(
                name: "ix_euronext_symbol_mappings_isin_mic",
                table: "euronext_symbol_mappings",
                columns: new[] { "isin", "mic" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "euronext_symbol_mappings");
        }
    }
}
