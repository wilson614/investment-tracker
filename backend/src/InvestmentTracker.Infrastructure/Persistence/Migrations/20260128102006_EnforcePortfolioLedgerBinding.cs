using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePortfolioLedgerBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // Step 1: Bind portfolios to existing USD ledgers where possible (first portfolio gets first ledger)
            migrationBuilder.Sql(@"
                UPDATE portfolios p
                SET ""BoundCurrencyLedgerId"" = (
                    SELECT cl.""Id""
                    FROM currency_ledgers cl
                    WHERE cl.""UserId"" = p.""UserId""
                      AND NOT EXISTS (
                          SELECT 1 FROM portfolios p2
                          WHERE p2.""BoundCurrencyLedgerId"" = cl.""Id""
                            AND p2.""Id"" != p.""Id""
                      )
                    ORDER BY cl.""CreatedAt"" ASC
                    LIMIT 1
                )
                WHERE p.""BoundCurrencyLedgerId"" IS NULL;
            ");

            // Step 2: For remaining portfolios, create new USD ledgers
            migrationBuilder.Sql(@"
                INSERT INTO currency_ledgers (""Id"", ""UserId"", ""CurrencyCode"", ""Name"", ""HomeCurrency"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    gen_random_uuid(),
                    p.""UserId"",
                    'USD',
                    'Auto-created for portfolio #' || ROW_NUMBER() OVER (PARTITION BY p.""UserId"" ORDER BY p.""CreatedAt""),
                    'TWD',
                    true,
                    NOW(),
                    NOW()
                FROM portfolios p
                WHERE p.""BoundCurrencyLedgerId"" IS NULL;
            ");

            // Step 3: Bind the newly created ledgers to remaining portfolios
            migrationBuilder.Sql(@"
                UPDATE portfolios p
                SET ""BoundCurrencyLedgerId"" = (
                    SELECT cl.""Id""
                    FROM currency_ledgers cl
                    WHERE cl.""UserId"" = p.""UserId""
                      AND cl.""Name"" LIKE 'Auto-created for portfolio #%'
                      AND NOT EXISTS (
                          SELECT 1 FROM portfolios p2
                          WHERE p2.""BoundCurrencyLedgerId"" = cl.""Id""
                      )
                    ORDER BY cl.""CreatedAt"" DESC
                    LIMIT 1
                )
                WHERE p.""BoundCurrencyLedgerId"" IS NULL;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios");

            migrationBuilder.DropIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios");

            migrationBuilder.AlterColumn<Guid>(
                name: "BoundCurrencyLedgerId",
                table: "portfolios",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId",
                principalTable: "currency_ledgers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios");

            migrationBuilder.DropIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios");

            migrationBuilder.AlterColumn<Guid>(
                name: "BoundCurrencyLedgerId",
                table: "portfolios",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_portfolios_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId");

            migrationBuilder.AddForeignKey(
                name: "FK_portfolios_currency_ledgers_BoundCurrencyLedgerId",
                table: "portfolios",
                column: "BoundCurrencyLedgerId",
                principalTable: "currency_ledgers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

        }
    }
}
