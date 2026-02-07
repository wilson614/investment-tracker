using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDisposableToFundAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDisposable",
                table: "fund_allocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // EmergencyFund (1), FamilyDeposit (2) are non-disposable; others are disposable.
            migrationBuilder.Sql(@"
                UPDATE fund_allocations
                SET ""IsDisposable"" = CASE
                    WHEN ""Purpose"" IN (1, 2) THEN FALSE
                    ELSE TRUE
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDisposable",
                table: "fund_allocations");
        }
    }
}
