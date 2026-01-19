namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 取得市場指數價格以進行 CAPE 調整的服務。
/// </summary>
public interface IIndexPriceService
{
    /// <summary>
    /// 取得 CAPE 調整所需的指數即時價與參考歷史價。
    /// </summary>
    Task<IndexPriceData?> GetIndexPricesAsync(string marketKey, DateTime referenceDate, CancellationToken cancellationToken = default);
}

/// <summary>
/// 用於 CAPE 調整計算的指數價格資料。
/// </summary>
public record IndexPriceData(
    string MarketKey,
    string IndexSymbol,
    decimal CurrentPrice,
    decimal ReferencePrice,
    DateTime ReferenceDate,
    DateTime FetchedAt
);
