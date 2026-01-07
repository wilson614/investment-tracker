using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Interface for stock price providers
/// </summary>
public interface IStockPriceProvider
{
    /// <summary>
    /// Get real-time stock quote
    /// </summary>
    Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if this provider supports the given market
    /// </summary>
    bool SupportsMarket(StockMarket market);
}
