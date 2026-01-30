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

            // 1.5) Clean up duplicate CurrencyLedgers before restoring unique index
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    r RECORD;
                    keeper_id uuid;
                    keeper_has_portfolio boolean;
                    portfolio_to_move_id uuid;
                BEGIN
                    -- Loop through each (UserId, CurrencyCode) that has duplicates
                    FOR r IN 
                        SELECT ""UserId"", ""CurrencyCode"", COUNT(*) 
                        FROM currency_ledgers 
                        GROUP BY ""UserId"", ""CurrencyCode"" 
                        HAVING COUNT(*) > 1
                    LOOP
                        -- Identify keeper: Oldest CreatedAt, then Smallest Id
                        SELECT ""Id"" INTO keeper_id
                        FROM currency_ledgers
                        WHERE ""UserId"" = r.""UserId"" AND ""CurrencyCode"" = r.""CurrencyCode""
                        ORDER BY ""CreatedAt"" ASC, ""Id""::text ASC
                        LIMIT 1;

                        -- 1. Move Transactions
                        UPDATE currency_transactions
                        SET ""CurrencyLedgerId"" = keeper_id
                        WHERE ""CurrencyLedgerId"" IN (
                            SELECT ""Id"" FROM currency_ledgers 
                            WHERE ""UserId"" = r.""UserId"" AND ""CurrencyCode"" = r.""CurrencyCode"" AND ""Id"" != keeper_id
                        );

                        -- 2. Handle Portfolios
                        -- Check if keeper already has a portfolio
                        SELECT EXISTS(SELECT 1 FROM portfolios WHERE ""BoundCurrencyLedgerId"" = keeper_id) INTO keeper_has_portfolio;

                        IF keeper_has_portfolio THEN
                            -- Keeper is taken. Any portfolios on duplicates must be deleted.
                            DELETE FROM portfolios
                            WHERE ""BoundCurrencyLedgerId"" IN (
                                SELECT ""Id"" FROM currency_ledgers 
                                WHERE ""UserId"" = r.""UserId"" AND ""CurrencyCode"" = r.""CurrencyCode"" AND ""Id"" != keeper_id
                            );
                        ELSE
                            -- Keeper is free.
                            -- Check if there are portfolios on duplicates. Pick one to survive.
                            SELECT ""Id"" INTO portfolio_to_move_id
                            FROM portfolios
                            WHERE ""BoundCurrencyLedgerId"" IN (
                                SELECT ""Id"" FROM currency_ledgers 
                                WHERE ""UserId"" = r.""UserId"" AND ""CurrencyCode"" = r.""CurrencyCode"" AND ""Id"" != keeper_id
                            )
                            ORDER BY ""CreatedAt"" ASC, ""Id""::text ASC
                            LIMIT 1;

                            IF portfolio_to_move_id IS NOT NULL THEN
                                -- Move the chosen one
                                UPDATE portfolios
                                SET ""BoundCurrencyLedgerId"" = keeper_id
                                WHERE ""Id"" = portfolio_to_move_id;

                                -- Delete any OTHER portfolios on duplicates (if any existed)
                                DELETE FROM portfolios
                                WHERE ""BoundCurrencyLedgerId"" IN (
                                    SELECT ""Id"" FROM currency_ledgers 
                                    WHERE ""UserId"" = r.""UserId"" AND ""CurrencyCode"" = r.""CurrencyCode"" AND ""Id"" != keeper_id
                                ) AND ""Id"" != portfolio_to_move_id;
                            END IF;
                        END IF;

                        -- 3. Delete the duplicate ledgers themselves
                        DELETE FROM currency_ledgers
                        WHERE ""UserId"" = r.""UserId"" 
                          AND ""CurrencyCode"" = r.""CurrencyCode"" 
                          AND ""Id"" != keeper_id;
                    END LOOP;
                END $$;
            ");

            // 2) Restore unique index on (UserId, CurrencyCode)
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CurrencyLedger_UserId_CurrencyCode"";");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_CurrencyLedger_UserId_CurrencyCode"" ON currency_ledgers (""UserId"", ""CurrencyCode"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
