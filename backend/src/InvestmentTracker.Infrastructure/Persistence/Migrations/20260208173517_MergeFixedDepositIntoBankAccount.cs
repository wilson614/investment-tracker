using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeFixedDepositIntoBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountType",
                table: "bank_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualInterest",
                table: "bank_accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedInterest",
                table: "bank_accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FixedDepositStatus",
                table: "bank_accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaturityDate",
                table: "bank_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "bank_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermMonths",
                table: "bank_accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                INSERT INTO bank_accounts (
                    ""Id"", ""UserId"", ""BankName"", ""TotalAssets"", ""InterestRate"", ""InterestCap"", ""Currency"", ""Note"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"",
                    ""AccountType"", ""TermMonths"", ""StartDate"", ""MaturityDate"", ""ExpectedInterest"", ""ActualInterest"", ""FixedDepositStatus""
                )
                SELECT
                    fd.""Id"", fd.""UserId"", ba.""BankName"", fd.""Principal"", fd.""AnnualInterestRate"", 0, fd.""Currency"", fd.""Note"", true, fd.""CreatedAt"", fd.""UpdatedAt"",
                    1, fd.""TermMonths"", fd.""StartDate"", fd.""MaturityDate"", fd.""ExpectedInterest"", fd.""ActualInterest"", fd.""Status""
                FROM fixed_deposits fd
                LEFT JOIN bank_accounts ba ON ba.""Id"" = fd.""BankAccountId"";
            ");

            migrationBuilder.DropTable(
                name: "fixed_deposits");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccount_AccountType",
                table: "bank_accounts",
                column: "AccountType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankAccount_AccountType",
                table: "bank_accounts");

            migrationBuilder.CreateTable(
                name: "fixed_deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualInterestRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TermMonths = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaturityDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpectedInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualInterest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fixed_deposits_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "bank_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedDeposit_BankAccountId",
                table: "fixed_deposits",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedDeposit_UserId",
                table: "fixed_deposits",
                column: "UserId");

            migrationBuilder.Sql(@"
                INSERT INTO fixed_deposits (
                    ""Id"", ""UserId"", ""BankAccountId"", ""Principal"", ""AnnualInterestRate"", ""TermMonths"", ""StartDate"", ""MaturityDate"", ""ExpectedInterest"", ""ActualInterest"", ""Currency"", ""Status"", ""Note"", ""CreatedAt"", ""UpdatedAt""
                )
                SELECT
                    ba.""Id"", ba.""UserId"", ba.""Id"", ba.""TotalAssets"", ba.""InterestRate"", ba.""TermMonths"", ba.""StartDate"", ba.""MaturityDate"", ba.""ExpectedInterest"", ba.""ActualInterest"", ba.""Currency"", ba.""FixedDepositStatus"", ba.""Note"", ba.""CreatedAt"", ba.""UpdatedAt""
                FROM bank_accounts ba
                WHERE ba.""AccountType"" = 1;
            ");

            migrationBuilder.Sql(@"
                DELETE FROM bank_accounts
                WHERE ""AccountType"" = 1;
            ");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "ActualInterest",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "ExpectedInterest",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "FixedDepositStatus",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "MaturityDate",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "TermMonths",
                table: "bank_accounts");
        }
    }
}
