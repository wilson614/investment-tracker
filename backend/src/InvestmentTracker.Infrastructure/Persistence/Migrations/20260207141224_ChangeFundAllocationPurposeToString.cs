using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeFundAllocationPurposeToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE fund_allocations
                ALTER COLUMN ""Purpose"" TYPE character varying(100)
                USING CASE ""Purpose""
                    WHEN 1 THEN '緊急預備金'
                    WHEN 2 THEN '家庭存款'
                    WHEN 3 THEN '一般用途'
                    WHEN 4 THEN '儲蓄'
                    WHEN 5 THEN '投資準備金'
                    WHEN 6 THEN '其他'
                    ELSE '其他'
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE fund_allocations
                ALTER COLUMN ""Purpose"" TYPE integer
                USING CASE ""Purpose""
                    WHEN '緊急預備金' THEN 1
                    WHEN '家庭存款' THEN 2
                    WHEN '一般用途' THEN 3
                    WHEN '儲蓄' THEN 4
                    WHEN '投資準備金' THEN 5
                    WHEN '其他' THEN 6
                    ELSE 6
                END
            ");
        }
    }
}
