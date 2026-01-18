using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Service for managing historical year-end data cache.
/// Provides lazy loading: check cache first, fetch from API if missing, save to cache.
/// </summary>
public class HistoricalYearEndDataService : IHistoricalYearEndDataService
{
    private readonly IHistoricalYearEndDataRepository _repository;
    private readonly IStooqHistoricalPriceService _stooqService;
    private readonly ITwseStockHistoricalPriceService _twseStockService;
    private readonly ILogger<HistoricalYearEndDataService> _logger;

    public HistoricalYearEndDataService(
        IHistoricalYearEndDataRepository repository,
        IStooqHistoricalPriceService stooqService,
        ITwseStockHistoricalPriceService twseStockService,
        ILogger<HistoricalYearEndDataService> logger)
    {
        _repository = repository;
        _stooqService = stooqService;
        _twseStockService = twseStockService;
        _logger = logger;
    }

    /// <summary>
    /// Gets year-end stock price from cache, or fetches from API and caches it.
    /// Returns null if price cannot be obtained (requires manual entry).
    /// </summary>
    public async Task<YearEndPriceResult?> GetOrFetchYearEndPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        // Never cache current year (YTD prices are still changing)
        var currentYear = DateTime.UtcNow.Year;
        if (year >= currentYear)
        {
            _logger.LogDebug("Year {Year} is current year or future - not caching", year);
            return await FetchPriceFromApiAsync(ticker, year, cancellationToken);
        }

        // Check cache first
        var cached = await _repository.GetStockPriceAsync(ticker, year, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for {Ticker}/{Year}: {Value}", ticker, year, cached.Value);
            return new YearEndPriceResult
            {
                Price = cached.Value,
                Currency = cached.Currency,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        // Fetch from API
        _logger.LogInformation("Cache miss for {Ticker}/{Year}, fetching from API...", ticker, year);
        var apiResult = await FetchPriceFromApiAsync(ticker, year, cancellationToken);

        if (apiResult != null)
        {
            // Save to cache
            try
            {
                var cacheEntry = HistoricalYearEndData.CreateStockPrice(
                    ticker,
                    year,
                    apiResult.Price,
                    apiResult.Currency,
                    apiResult.ActualDate,
                    apiResult.Source);

                await _repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                _logger.LogInformation("Cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
                    ticker, year, apiResult.Price, apiResult.Currency);
            }
            catch (InvalidOperationException ex)
            {
                // Entry might have been added by another request - this is fine
                _logger.LogDebug(ex, "Cache entry already exists for {Ticker}/{Year}", ticker, year);
            }
        }

        return apiResult;
    }

    /// <summary>
    /// Gets year-end exchange rate from cache, or fetches from API and caches it.
    /// Returns null if rate cannot be obtained (requires manual entry).
    /// </summary>
    public async Task<YearEndExchangeRateResult?> GetOrFetchYearEndExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        int year,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";

        // Never cache current year
        var currentYear = DateTime.UtcNow.Year;
        if (year >= currentYear)
        {
            _logger.LogDebug("Year {Year} is current year or future - not caching exchange rate", year);
            return await FetchExchangeRateFromApiAsync(fromCurrency, toCurrency, year, cancellationToken);
        }

        // Check cache first
        var cached = await _repository.GetExchangeRateAsync(currencyPair, year, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for exchange rate {CurrencyPair}/{Year}: {Value}", currencyPair, year, cached.Value);
            return new YearEndExchangeRateResult
            {
                Rate = cached.Value,
                CurrencyPair = currencyPair,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        // Fetch from API
        _logger.LogInformation("Cache miss for exchange rate {CurrencyPair}/{Year}, fetching from API...", currencyPair, year);
        var apiResult = await FetchExchangeRateFromApiAsync(fromCurrency, toCurrency, year, cancellationToken);

        if (apiResult != null)
        {
            // Save to cache
            try
            {
                var cacheEntry = HistoricalYearEndData.CreateExchangeRate(
                    currencyPair,
                    year,
                    apiResult.Rate,
                    apiResult.ActualDate,
                    apiResult.Source);

                await _repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                _logger.LogInformation("Cached year-end exchange rate for {CurrencyPair}/{Year}: {Rate}",
                    currencyPair, year, apiResult.Rate);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "Cache entry already exists for {CurrencyPair}/{Year}", currencyPair, year);
            }
        }

        return apiResult;
    }

    /// <summary>
    /// Manually saves a year-end price. Only allowed when no cache entry exists.
    /// </summary>
    public async Task<YearEndPriceResult> SaveManualPriceAsync(
        string ticker,
        int year,
        decimal price,
        string currency,
        DateTime actualDate,
        CancellationToken cancellationToken = default)
    {
        // Check if entry already exists
        if (await _repository.ExistsAsync(HistoricalDataType.StockPrice, ticker, year, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {ticker}/{year}. Manual entry only allowed for empty cache entries.");
        }

        var cacheEntry = HistoricalYearEndData.CreateStockPrice(
            ticker,
            year,
            price,
            currency,
            DateTime.SpecifyKind(actualDate, DateTimeKind.Utc),
            "Manual");

        await _repository.AddAsync(cacheEntry, cancellationToken);
        _logger.LogInformation("Manually cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
            ticker, year, price, currency);

        return new YearEndPriceResult
        {
            Price = price,
            Currency = currency,
            ActualDate = DateTime.SpecifyKind(actualDate, DateTimeKind.Utc),
            Source = "Manual",
            FromCache = true
        };
    }

    /// <summary>
    /// Manually saves a year-end exchange rate. Only allowed when no cache entry exists.
    /// </summary>
    public async Task<YearEndExchangeRateResult> SaveManualExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        int year,
        decimal rate,
        DateTime actualDate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";

        if (await _repository.ExistsAsync(HistoricalDataType.ExchangeRate, currencyPair, year, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {currencyPair}/{year}. Manual entry only allowed for empty cache entries.");
        }

        var cacheEntry = HistoricalYearEndData.CreateExchangeRate(
            currencyPair,
            year,
            rate,
            DateTime.SpecifyKind(actualDate, DateTimeKind.Utc),
            "Manual");

        await _repository.AddAsync(cacheEntry, cancellationToken);
        _logger.LogInformation("Manually cached year-end exchange rate for {CurrencyPair}/{Year}: {Rate}",
            currencyPair, year, rate);

        return new YearEndExchangeRateResult
        {
            Rate = rate,
            CurrencyPair = currencyPair,
            ActualDate = DateTime.SpecifyKind(actualDate, DateTimeKind.Utc),
            Source = "Manual",
            FromCache = true
        };
    }

    private async Task<YearEndPriceResult?> FetchPriceFromApiAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if this is a Taiwan stock (numeric ticker like 2330, 0050)
            if (IsTaiwanStock(ticker))
            {
                return await FetchTaiwanStockPriceAsync(ticker, year, cancellationToken);
            }

            // Use Dec 31 as target date (Stooq will find nearest trading day)
            var targetDate = new DateOnly(year, 12, 31);
            var result = await _stooqService.GetStockPriceAsync(ticker, targetDate, cancellationToken);

            if (result == null)
            {
                _logger.LogWarning("Could not fetch year-end price for {Ticker}/{Year} from Stooq", ticker, year);
                return null;
            }

            return new YearEndPriceResult
            {
                Price = result.Price,
                Currency = result.Currency,
                ActualDate = result.ActualDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Source = "Stooq",
                FromCache = false
            };
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching year-end price for {Ticker}/{Year}", ticker, year);
            return null;
        }
    }

    /// <summary>
    /// Determines if a ticker is a Taiwan stock (numeric format like 2330, 0050).
    /// </summary>
    private static bool IsTaiwanStock(string ticker)
    {
        // Taiwan stocks are typically 4-digit numeric codes (e.g., 2330, 0050, 2454)
        // Some may have suffixes like ".TW" which we strip
        var baseTicker = ticker.Split('.')[0];
        return baseTicker.Length >= 4 && baseTicker.Length <= 6 && baseTicker.All(char.IsDigit);
    }

    /// <summary>
    /// Fetches historical price for Taiwan stocks from TWSE.
    /// </summary>
    private async Task<YearEndPriceResult?> FetchTaiwanStockPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken)
    {
        var stockNo = ticker.Split('.')[0]; // Remove any suffix like ".TW"
        var result = await _twseStockService.GetYearEndPriceAsync(stockNo, year, cancellationToken);

        if (result == null)
        {
            _logger.LogWarning("Could not fetch year-end price for Taiwan stock {Ticker}/{Year} from TWSE", ticker, year);
            return null;
        }

        return new YearEndPriceResult
        {
            Price = result.Price,
            Currency = "TWD", // Taiwan stocks are always in TWD
            ActualDate = result.ActualDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Source = "TWSE",
            FromCache = false
        };
    }

    private async Task<YearEndExchangeRateResult?> FetchExchangeRateFromApiAsync(
        string fromCurrency,
        string toCurrency,
        int year,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = new DateOnly(year, 12, 31);
            var result = await _stooqService.GetExchangeRateAsync(fromCurrency, toCurrency, targetDate, cancellationToken);

            if (result == null)
            {
                _logger.LogWarning("Could not fetch year-end exchange rate for {From}/{To}/{Year} from Stooq",
                    fromCurrency, toCurrency, year);
                return null;
            }

            return new YearEndExchangeRateResult
            {
                Rate = result.Rate,
                CurrencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}",
                ActualDate = result.ActualDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Source = "Stooq",
                FromCache = false
            };
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching year-end exchange rate for {From}/{To}/{Year}",
                fromCurrency, toCurrency, year);
            return null;
        }
    }
}
