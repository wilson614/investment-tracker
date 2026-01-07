using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Service that aggregates multiple stock price providers
/// </summary>
public interface IStockPriceService
{
    Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default);
}

public class StockPriceService : IStockPriceService
{
    private readonly IEnumerable<IStockPriceProvider> _providers;
    private readonly ILogger<StockPriceService> _logger;

    public StockPriceService(IEnumerable<IStockPriceProvider> providers, ILogger<StockPriceService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(p => p.SupportsMarket(market));

        if (provider == null)
        {
            _logger.LogWarning("No provider found for market {Market}", market);
            return null;
        }

        return await provider.GetQuoteAsync(market, symbol, cancellationToken);
    }
}
