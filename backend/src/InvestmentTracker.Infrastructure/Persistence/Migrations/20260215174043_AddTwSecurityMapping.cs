using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTwSecurityMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tw_security_mappings",
                columns: table => new
                {
                    ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    security_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    isin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    market = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    currency = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tw_security_mappings", x => x.ticker);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tw_security_mappings_security_name",
                table: "tw_security_mappings",
                column: "security_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tw_security_mappings");
        }
    }
}
