using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Service that aggregates multiple stock price providers
/// </summary>
public interface IStockPriceService
{
    Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default);
    Task<StockQuoteResponse?> GetQuoteWithExchangeRateAsync(StockMarket market, string symbol, string homeCurrency, CancellationToken cancellationToken = default);
    Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
}

public class StockPriceService : IStockPriceService
{
    private readonly IEnumerable<IStockPriceProvider> _providers;
    private readonly IExchangeRateProvider _exchangeRateProvider;
    private readonly ILogger<StockPriceService> _logger;

    // Market to base currency mapping
    // Note: UK market also uses USD since most ETFs (VWRA, VUSA, etc.) are USD-denominated
    private static readonly Dictionary<StockMarket, string> MarketCurrencies = new()
    {
        { StockMarket.TW, "TWD" },
        { StockMarket.US, "USD" },
        { StockMarket.UK, "USD" }
    };

    public StockPriceService(
        IEnumerable<IStockPriceProvider> providers,
        IExchangeRateProvider exchangeRateProvider,
        ILogger<StockPriceService> logger)
    {
        _providers = providers;
        _exchangeRateProvider = exchangeRateProvider;
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

    public async Task<StockQuoteResponse?> GetQuoteWithExchangeRateAsync(
        StockMarket market,
        string symbol,
        string homeCurrency,
        CancellationToken cancellationToken = default)
    {
        var quote = await GetQuoteAsync(market, symbol, cancellationToken);
        if (quote == null) return null;

        var baseCurrency = MarketCurrencies.GetValueOrDefault(market, "USD");

        // If base currency is same as home currency, exchange rate is 1
        if (baseCurrency.Equals(homeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return quote with
            {
                ExchangeRate = 1m,
                ExchangeRatePair = $"{baseCurrency}/{homeCurrency}"
            };
        }

        // Fetch exchange rate
        var exchangeRate = await _exchangeRateProvider.GetExchangeRateAsync(baseCurrency, homeCurrency, cancellationToken);
        if (exchangeRate == null)
        {
            _logger.LogWarning("Failed to get exchange rate for {Base}/{Home}", baseCurrency, homeCurrency);
            return quote; // Return quote without exchange rate
        }

        return quote with
        {
            ExchangeRate = exchangeRate.Rate,
            ExchangeRatePair = $"{baseCurrency}/{homeCurrency}"
        };
    }

    public async Task<ExchangeRateResponse?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        return await _exchangeRateProvider.GetExchangeRateAsync(fromCurrency, toCurrency, cancellationToken);
    }
}
