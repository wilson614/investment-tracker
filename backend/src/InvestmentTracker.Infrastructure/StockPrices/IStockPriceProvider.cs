using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// 股價提供者（Stock Price Provider）介面。
/// </summary>
public interface IStockPriceProvider
{
    /// <summary>
    /// 取得即時報價。
    /// </summary>
    Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 判斷是否支援指定市場。
    /// </summary>
    bool SupportsMarket(StockMarket market);
}
