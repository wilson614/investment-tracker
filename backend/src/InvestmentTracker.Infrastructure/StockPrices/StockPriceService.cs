using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// 彙整多個股價與匯率來源的服務。
/// </summary>
public interface IStockPriceService
{
    Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default);
    Task<StockQuoteResponse?> GetQuoteWithExchangeRateAsync(StockMarket market, string symbol, string homeCurrency, CancellationToken cancellationToken = default);
    Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
}

public class StockPriceService(
    IEnumerable<IStockPriceProvider> providers,
    IExchangeRateProvider exchangeRateProvider,
    ILogger<StockPriceService> logger) : IStockPriceService
{
    // 市場對應的基準幣別
    // 注意：英國市場多數 ETF（如 VWRA、VUSA）以 USD 計價，因此此處也以 USD 作為基準幣別
    private static readonly Dictionary<StockMarket, string> MarketCurrencies = new()
    {
        [StockMarket.TW] = "TWD",
        [StockMarket.US] = "USD",
        [StockMarket.UK] = "USD"
    };

    public async Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(p => p.SupportsMarket(market));

        if (provider == null)
        {
            logger.LogWarning("No provider found for market {Market}", market);
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
        var exchangeRate = await exchangeRateProvider.GetExchangeRateAsync(baseCurrency, homeCurrency, cancellationToken);
        if (exchangeRate == null)
        {
            logger.LogWarning("Failed to get exchange rate for {Base}/{Home}", baseCurrency, homeCurrency);
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
        return await exchangeRateProvider.GetExchangeRateAsync(fromCurrency, toCurrency, cancellationToken);
    }
}
