using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 投資組合月度淨值快照（月底/當月截至今日）。
/// 用於 Dashboard 歷史淨值折線圖的快取資料。
/// </summary>
public class MonthlyNetWorthSnapshot : BaseEntity
{
    public Guid PortfolioId { get; private set; }

    /// <summary>
    /// 月份識別（必須為該月第一天，例如 2024-01-01）。
    /// </summary>
    public DateOnly Month { get; private set; }

    /// <summary>
    /// 估值（本位幣）。
    /// </summary>
    public decimal TotalValueHome { get; private set; }

    /// <summary>
    /// 累積淨投入（本位幣，Buy - Sell）。
    /// </summary>
    public decimal TotalContributions { get; private set; }

    /// <summary>
    /// 資料來源：Yahoo / Stooq / TWSE / Mixed / Calculated。
    /// </summary>
    public string DataSource { get; private set; } = string.Empty;

    /// <summary>
    /// 此快照實際計算時間（UTC）。
    /// </summary>
    public DateTime CalculatedAt { get; private set; }

    /// <summary>
    /// 可選：持倉與取價細節（JSON），用於除錯。
    /// </summary>
    public string? PositionDetails { get; private set; }

    // 導覽屬性
    public Portfolio Portfolio { get; private set; } = null!;

    // EF Core 必要的無參數建構子
    private MonthlyNetWorthSnapshot() { }

    public MonthlyNetWorthSnapshot(
        Guid portfolioId,
        DateOnly month,
        decimal totalValueHome,
        decimal totalContributions,
        string dataSource,
        DateTime calculatedAt,
        string? positionDetails = null)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required", nameof(portfolioId));

        if (month.Day != 1)
            throw new ArgumentException("Month must be the first day of month", nameof(month));

        if (totalValueHome < 0)
            throw new ArgumentException("Total value cannot be negative", nameof(totalValueHome));

        PortfolioId = portfolioId;
        Month = month;
        TotalValueHome = Math.Round(totalValueHome, 4);
        TotalContributions = Math.Round(totalContributions, 4);

        if (string.IsNullOrWhiteSpace(dataSource))
            throw new ArgumentException("DataSource is required", nameof(dataSource));

        DataSource = dataSource.Trim();
        CalculatedAt = DateTime.SpecifyKind(calculatedAt, DateTimeKind.Utc);
        PositionDetails = positionDetails;
    }
}
