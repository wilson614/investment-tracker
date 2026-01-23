using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 管理歷史年末資料快取的服務。
/// 採 lazy loading：先查快取，若缺少則從 API 抓取並寫入快取。
/// </summary>
public class HistoricalYearEndDataService(
    IHistoricalYearEndDataRepository repository,
    IStooqHistoricalPriceService stooqService,
    ITwseStockHistoricalPriceService twseStockService,
    IYahooHistoricalPriceService yahooService,
    ILogger<HistoricalYearEndDataService> logger) : IHistoricalYearEndDataService
{
    /// <summary>
    /// Gets year-end stock price from cache, or fetches from API and caches it.
    /// Returns null if price cannot be obtained (requires manual entry).
    /// </summary>
    public async Task<YearEndPriceResult?> GetOrFetchYearEndPriceAsync(
        string ticker,
        int year,
        StockMarket? market = null,
        CancellationToken cancellationToken = default)
    {
        // 不快取當年度（YTD 價格仍在變動）- 直接回傳 null，讓前端用即時報價
        var currentYear = DateTime.UtcNow.Year;
        if (year >= currentYear)
        {
            logger.LogDebug("Year {Year} is current year or future - returning null (frontend should provide live price)", year);
            return null;
        }

        // 先查快取
        var cached = await repository.GetStockPriceAsync(ticker, year, cancellationToken);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for {Ticker}/{Year}: {Value}", ticker, year, cached.Value);
            return new YearEndPriceResult
            {
                Price = cached.Value,
                Currency = cached.Currency,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        // 從 API 抓取
        logger.LogInformation("Cache miss for {Ticker}/{Year}, fetching from API...", ticker, year);
        var apiResult = await FetchPriceFromApiAsync(ticker, year, market, cancellationToken);

        if (apiResult != null)
        {
            // 寫入快取
            try
            {
                var cacheEntry = HistoricalYearEndData.CreateStockPrice(
                    ticker,
                    year,
                    apiResult.Price,
                    apiResult.Currency,
                    apiResult.ActualDate,
                    apiResult.Source);

                await repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                logger.LogInformation("Cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
                    ticker, year, apiResult.Price, apiResult.Currency);
            }
            catch (InvalidOperationException ex)
            {
                // 可能已被其他並發請求寫入；可忽略
                logger.LogDebug(ex, "Cache entry already exists for {Ticker}/{Year}", ticker, year);
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

        // 不快取當年度 - 直接回傳 null，讓前端用即時匯率
        var currentYear = DateTime.UtcNow.Year;
        if (year >= currentYear)
        {
            logger.LogDebug("Year {Year} is current year or future - returning null for exchange rate", year);
            return null;
        }

        // 先查快取
        var cached = await repository.GetExchangeRateAsync(currencyPair, year, cancellationToken);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for exchange rate {CurrencyPair}/{Year}: {Value}", currencyPair, year, cached.Value);
            return new YearEndExchangeRateResult
            {
                Rate = cached.Value,
                CurrencyPair = currencyPair,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        // 從 API 抓取
        logger.LogInformation("Cache miss for exchange rate {CurrencyPair}/{Year}, fetching from API...", currencyPair, year);
        var apiResult = await FetchExchangeRateFromApiAsync(fromCurrency, toCurrency, year, cancellationToken);

        if (apiResult != null)
        {
            // 寫入快取
            try
            {
                var cacheEntry = HistoricalYearEndData.CreateExchangeRate(
                    currencyPair,
                    year,
                    apiResult.Rate,
                    apiResult.ActualDate,
                    apiResult.Source);

                await repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                logger.LogInformation("Cached year-end exchange rate for {CurrencyPair}/{Year}: {Rate}",
                    currencyPair, year, apiResult.Rate);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogDebug(ex, "Cache entry already exists for {CurrencyPair}/{Year}", currencyPair, year);
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
        if (await repository.ExistsAsync(HistoricalDataType.StockPrice, ticker, year, cancellationToken))
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

        await repository.AddAsync(cacheEntry, cancellationToken);
        logger.LogInformation("Manually cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
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

        if (await repository.ExistsAsync(HistoricalDataType.ExchangeRate, currencyPair, year, cancellationToken))
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

        await repository.AddAsync(cacheEntry, cancellationToken);
        logger.LogInformation("Manually cached year-end exchange rate for {CurrencyPair}/{Year}: {Rate}",
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
        StockMarket? market,
        CancellationToken cancellationToken)
    {
        try
        {
            // 根據 market 參數判斷資料來源
            // 台股：使用 TWSE API
            if (market == StockMarket.TW || (market == null && IsTaiwanStock(ticker)))
            {
                return await FetchTaiwanStockPriceAsync(ticker, year, cancellationToken);
            }

            // 歐股、美股、英股：使用 Yahoo Finance 為 primary，Stooq 為 fallback
            var targetDate = new DateOnly(year, 12, 31);

            // Try Yahoo first (primary source)
            var yahooResult = await TryFetchFromYahooAsync(ticker, targetDate, market, cancellationToken);
            if (yahooResult != null)
            {
                logger.LogInformation("Fetched {Ticker}/{Year} from Yahoo (primary): {Price} {Currency}",
                    ticker, year, yahooResult.Price, yahooResult.Currency);
                return yahooResult;
            }

            // Fallback to Stooq (except for EU market which Yahoo should handle)
            if (market != StockMarket.EU)
            {
                var stooqResult = await TryFetchFromStooqAsync(ticker, targetDate, cancellationToken);
                if (stooqResult != null)
                {
                    logger.LogInformation("Fetched {Ticker}/{Year} from Stooq (fallback): {Price} {Currency}",
                        ticker, year, stooqResult.Price, stooqResult.Currency);
                    return stooqResult;
                }
            }

            logger.LogWarning("Could not fetch year-end price for {Ticker}/{Year} from any source (Yahoo and Stooq both failed)",
                ticker, year);
            return null;
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching year-end price for {Ticker}/{Year}", ticker, year);
            return null;
        }
    }

    /// <summary>
    /// Try to fetch price from Yahoo Finance.
    /// </summary>
    private async Task<YearEndPriceResult?> TryFetchFromYahooAsync(
        string ticker,
        DateOnly targetDate,
        StockMarket? market,
        CancellationToken cancellationToken)
    {
        try
        {
            // Convert ticker to Yahoo format if needed
            var yahooSymbol = ConvertToYahooSymbol(ticker, market);
            var result = await yahooService.GetHistoricalPriceAsync(yahooSymbol, targetDate, cancellationToken);

            if (result == null)
            {
                logger.LogDebug("Yahoo returned no data for {Ticker} on {Date}", yahooSymbol, targetDate);
                return null;
            }

            return new YearEndPriceResult
            {
                Price = result.Price,
                Currency = result.Currency,
                ActualDate = result.ActualDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Source = "Yahoo",
                FromCache = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Yahoo fetch failed for {Ticker}, will try fallback", ticker);
            return null;
        }
    }

    /// <summary>
    /// Try to fetch price from Stooq.
    /// </summary>
    private async Task<YearEndPriceResult?> TryFetchFromStooqAsync(
        string ticker,
        DateOnly targetDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await stooqService.GetStockPriceAsync(ticker, targetDate, cancellationToken);

            if (result == null)
            {
                logger.LogDebug("Stooq returned no data for {Ticker} on {Date}", ticker, targetDate);
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
            throw; // Re-throw rate limit exception
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stooq fetch failed for {Ticker}", ticker);
            return null;
        }
    }

    /// <summary>
    /// Convert ticker to Yahoo Finance symbol format based on market.
    /// </summary>
    private static string ConvertToYahooSymbol(string ticker, StockMarket? market)
    {
        return YahooSymbolHelper.ConvertToYahooSymbol(ticker, market);
    }

    /// <summary>
    /// 判斷 ticker 是否為台股代號（例如 2330、0050 等純數字格式）。
    /// </summary>
    private static bool IsTaiwanStock(string ticker)
    {
        // 台股通常是 4 位數（例如 2330、0050、2454）
        // 可能帶有 ".TW" 等後綴，這裡會先去除
        var baseTicker = ticker.Split('.')[0];
        return baseTicker.Length is >= 4 and <= 6 && baseTicker.All(char.IsDigit);
    }

    /// <summary>
    /// 從 TWSE 取得台股歷史價格。
    /// </summary>
    private async Task<YearEndPriceResult?> FetchTaiwanStockPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken)
    {
        var stockNo = ticker.Split('.')[0]; // 去除 ".TW" 等後綴
        var result = await twseStockService.GetYearEndPriceAsync(stockNo, year, cancellationToken);

        if (result == null)
        {
            logger.LogWarning("Could not fetch year-end price for Taiwan stock {Ticker}/{Year} from TWSE", ticker, year);
            return null;
        }

        return new YearEndPriceResult
        {
            Price = result.Price,
            Currency = "TWD", // 台股皆以 TWD 計價
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
            var result = await stooqService.GetExchangeRateAsync(fromCurrency, toCurrency, targetDate, cancellationToken);

            if (result == null)
            {
                logger.LogWarning("Could not fetch year-end exchange rate for {From}/{To}/{Year} from Stooq",
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
            logger.LogWarning(ex, "Error fetching year-end exchange rate for {From}/{To}/{Year}",
                fromCurrency, toCurrency, year);
            return null;
        }
    }
}
