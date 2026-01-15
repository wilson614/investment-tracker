using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEuronextQuoteCacheChangeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Change",
                table: "euronext_quote_cache",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangePercent",
                table: "euronext_quote_cache",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Change",
                table: "euronext_quote_cache");

            migrationBuilder.DropColumn(
                name: "ChangePercent",
                table: "euronext_quote_cache");
        }
    }
}
