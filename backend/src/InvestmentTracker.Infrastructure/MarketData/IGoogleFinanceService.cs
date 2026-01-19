namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Google Finance 取得指數即時價格的服務。
/// </summary>
public interface IGoogleFinanceService
{
    /// <summary>
    /// 取得指定 marketKey 的指數即時價格。
    /// </summary>
    Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default);
}
