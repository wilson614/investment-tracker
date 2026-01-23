namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 單一月份的淨值與累積淨投入資料。
/// </summary>
public record MonthlyNetWorthDto
{
    /// <summary>
    /// 月份標籤（YYYY-MM）。
    /// </summary>
    public string Month { get; init; } = string.Empty;

    /// <summary>
    /// 月底／截至日淨值（本位幣）。
    /// 若該月份無法取得足夠價格或匯率資料，可能為 null。
    /// </summary>
    public decimal? Value { get; init; }

    /// <summary>
    /// 累積淨投入（本位幣，Buy - Sell）。
    /// 若該月份無法取得足夠匯率資料，可能為 null。
    /// </summary>
    public decimal? Contributions { get; init; }
}

/// <summary>
/// 月度淨值歷史資料（用於 Dashboard 折線圖）。
/// </summary>
public record MonthlyNetWorthHistoryDto
{
    public IReadOnlyList<MonthlyNetWorthDto> Data { get; init; } = [];

    /// <summary>
    /// 回傳資料的幣別（本位幣，例如 TWD）。
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// 總月份數（等於 Data.Count）。
    /// </summary>
    public int TotalMonths { get; init; }

    /// <summary>
    /// 資料來源摘要：Yahoo / Stooq / TWSE / Mixed / Calculated。
    /// </summary>
    public string DataSource { get; init; } = "Mixed";
}
