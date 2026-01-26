using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 基準（Benchmark）年度報酬快取。
/// 主要用於歷史年度的 Total Return（含股息）比較。
/// </summary>
public class BenchmarkAnnualReturn : BaseEntity
{
    public string Symbol { get; private set; } = string.Empty;
    public int Year { get; private set; }

    /// <summary>
    /// 年度 Total Return（含股息），百分比表示（例如 12.34 代表 12.34%）。
    /// </summary>
    public decimal TotalReturnPercent { get; private set; }

    /// <summary>
    /// 年度 Price Return（不含股息），可選。
    /// </summary>
    public decimal? PriceReturnPercent { get; private set; }

    /// <summary>
    /// 資料來源：Yahoo / Calculated。
    /// </summary>
    public string DataSource { get; private set; } = string.Empty;

    /// <summary>
    /// 此筆資料取得時間（UTC）。
    /// </summary>
    public DateTime FetchedAt { get; private set; }

    private BenchmarkAnnualReturn() { }

    public BenchmarkAnnualReturn(
        string symbol,
        int year,
        decimal totalReturnPercent,
        string dataSource,
        DateTime fetchedAt,
        decimal? priceReturnPercent = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (year < 2000 || year > DateTime.UtcNow.Year)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year is out of supported range");

        Symbol = symbol.Trim().ToUpperInvariant();
        Year = year;

        TotalReturnPercent = Math.Round(totalReturnPercent, 4);
        PriceReturnPercent = priceReturnPercent.HasValue ? Math.Round(priceReturnPercent.Value, 4) : null;

        if (string.IsNullOrWhiteSpace(dataSource))
            throw new ArgumentException("DataSource is required", nameof(dataSource));

        DataSource = dataSource.Trim();
        FetchedAt = DateTime.SpecifyKind(fetchedAt, DateTimeKind.Utc);
    }
}
