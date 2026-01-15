using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Service for fetching and caching Euronext stock quotes.
/// Handles quote caching and exchange rate conversion.
/// </summary>
public class EuronextQuoteService
{
    private readonly IEuronextApiClient _apiClient;
    private readonly IEuronextQuoteCacheRepository _cacheRepository;
    private readonly IExchangeRateProvider _exchangeRateProvider;
    private readonly ILogger<EuronextQuoteService> _logger;

    // Cache duration in minutes (15 minutes during market hours)
    private const int CacheMinutes = 15;

    public EuronextQuoteService(
        IEuronextApiClient apiClient,
        IEuronextQuoteCacheRepository cacheRepository,
        IExchangeRateProvider exchangeRateProvider,
        ILogger<EuronextQuoteService> logger)
    {
        _apiClient = apiClient;
        _cacheRepository = cacheRepository;
        _exchangeRateProvider = exchangeRateProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get quote for a Euronext-listed stock with optional exchange rate to home currency.
    /// </summary>
    /// <param name="isin">ISIN code (e.g., IE000FHBZDZ8 for AGAC)</param>
    /// <param name="mic">Market Identifier Code (e.g., XAMS for Amsterdam)</param>
    /// <param name="homeCurrency">Target currency for exchange rate (default: TWD)</param>
    /// <param name="forceRefresh">Whether to bypass cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quote result with price and exchange rate</returns>
    public async Task<EuronextQuoteResult?> GetQuoteAsync(
        string isin,
        string mic,
        string homeCurrency = "TWD",
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get from cache first
            if (!forceRefresh)
            {
                var cached = await _cacheRepository.GetByIsinAndMicAsync(isin, mic, cancellationToken);
                if (cached != null && !cached.IsStale && !IsCacheExpired(cached))
                {
                    _logger.LogDebug("Using cached quote for {Isin}-{Mic}", isin, mic);

                    // Get exchange rate for cached quote
                    var rate = await GetExchangeRateAsync(cached.Currency, homeCurrency, cancellationToken);

                    return new EuronextQuoteResult(
                        cached.Price,
                        cached.Currency,
                        cached.MarketTime,
                        cached.Isin,
                        rate,
                        true);
                }
            }

            // Fetch fresh quote
            _logger.LogInformation("Fetching fresh quote for {Isin}-{Mic}", isin, mic);
            var quote = await _apiClient.GetQuoteAsync(isin, mic, cancellationToken);

            if (quote == null)
            {
                _logger.LogWarning("No quote returned for {Isin}-{Mic}", isin, mic);
                return null;
            }

            // Cache the result
            var cacheEntry = new EuronextQuoteCache(
                isin,
                mic,
                quote.Price,
                quote.Currency,
                quote.MarketTime ?? DateTime.UtcNow);

            await _cacheRepository.UpsertAsync(cacheEntry, cancellationToken);

            // Get exchange rate
            var exchangeRate = await GetExchangeRateAsync(quote.Currency, homeCurrency, cancellationToken);

            return new EuronextQuoteResult(
                quote.Price,
                quote.Currency,
                quote.MarketTime,
                quote.Name,
                exchangeRate,
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quote for {Isin}-{Mic}", isin, mic);
            return null;
        }
    }

    private bool IsCacheExpired(EuronextQuoteCache cached)
    {
        var age = DateTime.UtcNow - cached.FetchedAt;
        return age.TotalMinutes > CacheMinutes;
    }

    private async Task<decimal?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken)
    {
        if (fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        try
        {
            var response = await _exchangeRateProvider.GetExchangeRateAsync(fromCurrency, toCurrency, cancellationToken);
            return response?.Rate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get exchange rate from {From} to {To}", fromCurrency, toCurrency);
            return null;
        }
    }
}

/// <summary>
/// Result of a Euronext quote fetch with exchange rate.
/// </summary>
public record EuronextQuoteResult(
    decimal Price,
    string Currency,
    DateTime? MarketTime,
    string? Name,
    decimal? ExchangeRate,
    bool FromCache);
