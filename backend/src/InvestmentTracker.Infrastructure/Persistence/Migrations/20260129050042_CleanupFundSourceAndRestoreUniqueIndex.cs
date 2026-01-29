using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CleanupFundSourceAndRestoreUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Remove obsolete FundSource column (safe for DBs that may already lack it)
            migrationBuilder.Sql(@"ALTER TABLE stock_transactions DROP COLUMN IF EXISTS ""FundSource"";");

            // 2) Restore unique index on (UserId, CurrencyCode)
            // Some environments may have drifted (e.g., DB created via EnsureCreated + migration history sync).
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CurrencyLedger_UserId_CurrencyCode"";");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_CurrencyLedger_UserId_CurrencyCode"" ON currency_ledgers (""UserId"", ""CurrencyCode"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort rollback (Down migrations are rarely used in production)
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CurrencyLedger_UserId_CurrencyCode"";");

            migrationBuilder.AddColumn<int>(
                name: "FundSource",
                table: "stock_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
