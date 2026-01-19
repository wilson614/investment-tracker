namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Research Affiliates 取得 CAPE（Cyclically Adjusted P/E）資料的服務。
/// </summary>
public interface ICapeDataService
{
    /// <summary>
    /// 取得最新 CAPE 資料；若可用則使用快取。
    /// </summary>
    Task<CapeDataResponse?> GetCapeDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除快取並抓取最新資料。
    /// </summary>
    Task<CapeDataResponse?> RefreshCapeDataAsync(CancellationToken cancellationToken = default);
}

public record CapeDataResponse(
    string Date,
    List<CapeDataItem> Items,
    DateTime FetchedAt
);

public record CapeDataItem(
    string BoxName,
    decimal CurrentValue,
    decimal? AdjustedValue,
    decimal CurrentValuePercentile,
    decimal Range25th,
    decimal Range50th,
    decimal Range75th
);
