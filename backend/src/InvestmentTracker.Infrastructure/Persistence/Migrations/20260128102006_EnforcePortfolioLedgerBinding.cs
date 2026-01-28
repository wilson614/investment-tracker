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
