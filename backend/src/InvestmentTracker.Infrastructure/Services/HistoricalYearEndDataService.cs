using System.Collections.Concurrent;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.EntityFrameworkCore;
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
    // key: stock cache ticker + year，避免同一個年末價 cache miss 時被併發重複抓取
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> YearEndPriceInFlightLocks = new(StringComparer.Ordinal);
    private static readonly object YearEndPriceInFlightLocksSync = new();

    // key: currencyPair + year，避免同一個年末匯率 cache miss 時被併發重複抓取
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> YearEndExchangeRateInFlightLocks = new(StringComparer.Ordinal);
    private static readonly object YearEndExchangeRateInFlightLocksSync = new();

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

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var stockCacheTicker = BuildStockCacheTickerKey(normalizedTicker, market);
        var legacyStockCacheTicker = normalizedTicker;

        // 先查快取
        var cached = await repository.GetStockPriceAsync(stockCacheTicker, year, cancellationToken);
        if (cached == null && !string.Equals(stockCacheTicker, legacyStockCacheTicker, StringComparison.Ordinal))
        {
            cached = await repository.GetStockPriceAsync(legacyStockCacheTicker, year, cancellationToken);
        }

        if (cached != null)
        {
            logger.LogDebug("Cache hit for {Ticker}/{Year}: {Value}", stockCacheTicker, year, cached.Value);
            return new YearEndPriceResult
            {
                Price = cached.Value,
                Currency = cached.Currency,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        var lockKey = $"{stockCacheTicker}:{year}";
        var lease = AcquireLock(YearEndPriceInFlightLocks, YearEndPriceInFlightLocksSync, lockKey);
        var entered = false;

        try
        {
            await lease.Gate.WaitAsync(cancellationToken);
            entered = true;

            // double-check cache after entering gate
            cached = await repository.GetStockPriceAsync(stockCacheTicker, year, cancellationToken);
            if (cached == null && !string.Equals(stockCacheTicker, legacyStockCacheTicker, StringComparison.Ordinal))
            {
                cached = await repository.GetStockPriceAsync(legacyStockCacheTicker, year, cancellationToken);
            }

            if (cached != null)
            {
                logger.LogDebug("Cache hit (after wait) for {Ticker}/{Year}: {Value}", stockCacheTicker, year, cached.Value);
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
            logger.LogInformation("Cache miss for {Ticker}/{Year}, fetching from API...", stockCacheTicker, year);
            var apiResult = await FetchPriceFromApiAsync(normalizedTicker, year, market, cancellationToken);

            if (apiResult != null)
            {
                // 寫入快取
                try
                {
                    var cacheEntry = HistoricalYearEndData.CreateStockPrice(
                        stockCacheTicker,
                        year,
                        apiResult.Price,
                        apiResult.Currency,
                        apiResult.ActualDate,
                        apiResult.Source);

                    await repository.AddAsync(cacheEntry, cancellationToken);
                    apiResult.FromCache = false;
                    logger.LogInformation("Cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
                        stockCacheTicker, year, apiResult.Price, apiResult.Currency);
                }
                catch (InvalidOperationException ex)
                {
                    // 可能已被其他並發請求寫入；可忽略
                    logger.LogDebug(ex, "Cache entry already exists for {Ticker}/{Year}", stockCacheTicker, year);
                }
                catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
                {
                    // 可能已被其他並發請求寫入；可忽略
                    logger.LogDebug(ex, "Duplicate cache entry ignored for {Ticker}/{Year}", stockCacheTicker, year);
                }
            }

            return apiResult;
        }
        finally
        {
            if (entered)
            {
                lease.Gate.Release();
            }

            ReleaseLock(YearEndPriceInFlightLocks, YearEndPriceInFlightLocksSync, lockKey, lease);
        }
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
        var normalizedFromCurrency = NormalizeCurrencyCode(fromCurrency);
        var normalizedToCurrency = NormalizeCurrencyCode(toCurrency);
        var currencyPair = BuildCurrencyPairKey(normalizedFromCurrency, normalizedToCurrency);
        var legacyCurrencyPair = $"{normalizedFromCurrency}{normalizedToCurrency}";

        // 不快取當年度 - 直接回傳 null，讓前端用即時匯率
        var currentYear = DateTime.UtcNow.Year;
        if (year >= currentYear)
        {
            logger.LogDebug("Year {Year} is current year or future - returning null for exchange rate", year);
            return null;
        }

        // 先查快取
        var cached = await repository.GetExchangeRateAsync(currencyPair, year, cancellationToken);
        if (cached == null && !string.Equals(currencyPair, legacyCurrencyPair, StringComparison.Ordinal))
        {
            cached = await repository.GetExchangeRateAsync(legacyCurrencyPair, year, cancellationToken);
        }

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

        var lockKey = $"{currencyPair}:{year}";
        var lease = AcquireLock(YearEndExchangeRateInFlightLocks, YearEndExchangeRateInFlightLocksSync, lockKey);
        var entered = false;

        try
        {
            await lease.Gate.WaitAsync(cancellationToken);
            entered = true;

            // double-check cache after entering gate
            cached = await repository.GetExchangeRateAsync(currencyPair, year, cancellationToken);
            if (cached == null && !string.Equals(currencyPair, legacyCurrencyPair, StringComparison.Ordinal))
            {
                cached = await repository.GetExchangeRateAsync(legacyCurrencyPair, year, cancellationToken);
            }

            if (cached != null)
            {
                logger.LogDebug("Cache hit (after wait) for exchange rate {CurrencyPair}/{Year}: {Value}",
                    currencyPair, year, cached.Value);
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
            var apiResult = await FetchExchangeRateFromApiAsync(normalizedFromCurrency, normalizedToCurrency, year, cancellationToken);

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
                    // 可能已被其他並發請求寫入；可忽略
                    logger.LogDebug(ex, "Cache entry already exists for {CurrencyPair}/{Year}", currencyPair, year);
                }
                catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
                {
                    // 可能已被其他並發請求寫入；可忽略
                    logger.LogDebug(ex, "Duplicate cache entry ignored for {CurrencyPair}/{Year}", currencyPair, year);
                }
            }

            return apiResult;
        }
        finally
        {
            if (entered)
            {
                lease.Gate.Release();
            }

            ReleaseLock(YearEndExchangeRateInFlightLocks, YearEndExchangeRateInFlightLocksSync, lockKey, lease);
        }
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
        var normalizedTicker = ticker.Trim().ToUpperInvariant();

        // Keep manual-save conflict detection coherent with market-scoped stock cache keys.
        foreach (var cacheTicker in GetManualStockCacheTickersToCheck(normalizedTicker))
        {
            if (await repository.ExistsAsync(HistoricalDataType.StockPrice, cacheTicker, year, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Cache entry already exists for {cacheTicker}/{year}. Manual entry only allowed for empty cache entries.");
            }
        }

        // Keep persisted key backward compatible: manual entries are still stored under legacy ticker.
        var cacheEntry = HistoricalYearEndData.CreateStockPrice(
            normalizedTicker,
            year,
            price,
            currency,
            DateTime.SpecifyKind(actualDate, DateTimeKind.Utc),
            "Manual");

        await repository.AddAsync(cacheEntry, cancellationToken);
        logger.LogInformation("Manually cached year-end price for {Ticker}/{Year}: {Price} {Currency}",
            normalizedTicker, year, price, currency);

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
        var normalizedFromCurrency = NormalizeCurrencyCode(fromCurrency);
        var normalizedToCurrency = NormalizeCurrencyCode(toCurrency);
        var currencyPair = BuildCurrencyPairKey(normalizedFromCurrency, normalizedToCurrency);

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

    private static RefCountedSemaphore AcquireLock(
        ConcurrentDictionary<string, RefCountedSemaphore> locks,
        object syncRoot,
        string lockKey)
    {
        lock (syncRoot)
        {
            var entry = locks.GetOrAdd(lockKey, static _ => new RefCountedSemaphore());
            entry.RefCount++;
            return entry;
        }
    }

    private static void ReleaseLock(
        ConcurrentDictionary<string, RefCountedSemaphore> locks,
        object syncRoot,
        string lockKey,
        RefCountedSemaphore entry)
    {
        lock (syncRoot)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                locks.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(lockKey, entry));
            }
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true ||
               ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal) == true;
    }

    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int RefCount;
    }

    private async Task<YearEndPriceResult?> FetchPriceFromApiAsync(
        string ticker,
        int year,
        StockMarket? market,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = new DateOnly(year, 12, 31);
            var isTaiwanStock = market == StockMarket.TW || (market == null && IsTaiwanStock(ticker));

            // 歷史年末價格統一採 Yahoo 為 primary（含台股）
            var yahooResult = await TryFetchFromYahooAsync(ticker, targetDate, market, isTaiwanStock, cancellationToken);
            if (yahooResult != null)
            {
                logger.LogInformation("Fetched {Ticker}/{Year} from Yahoo (primary): {Price} {Currency}",
                    ticker, year, yahooResult.Price, yahooResult.Currency);
                return yahooResult;
            }

            if (isTaiwanStock)
            {
                var taiwanResult = await FetchTaiwanStockPriceAsync(ticker, year, cancellationToken);
                if (taiwanResult != null)
                {
                    logger.LogInformation("Fetched {Ticker}/{Year} from {Source} (fallback): {Price} {Currency}",
                        ticker, year, taiwanResult.Source, taiwanResult.Price, taiwanResult.Currency);
                    return taiwanResult;
                }

                logger.LogWarning("Could not fetch year-end price for Taiwan stock {Ticker}/{Year} from any source (Yahoo and TWSE/TPEx both failed)",
                    ticker, year);
                return null;
            }

            // 非台股僅 US/UK 允許 Stooq fallback；TW/EU 與未知市場皆不 fallback 到 Stooq
            if (market is StockMarket.US or StockMarket.UK)
            {
                var stooqResult = await TryFetchFromStooqAsync(ticker, targetDate, cancellationToken);
                if (stooqResult != null)
                {
                    logger.LogInformation("Fetched {Ticker}/{Year} from Stooq (fallback): {Price} {Currency}",
                        ticker, year, stooqResult.Price, stooqResult.Currency);
                    return stooqResult;
                }
            }

            logger.LogWarning("Could not fetch year-end price for {Ticker}/{Year} from any source (Yahoo primary, Stooq fallback only for US/UK)",
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
        bool isTaiwanStock,
        CancellationToken cancellationToken)
    {
        foreach (var yahooSymbol in GetYahooSymbolsForHistoricalLookup(ticker, market, isTaiwanStock))
        {
            try
            {
                var result = await yahooService.GetHistoricalPriceAsync(yahooSymbol, targetDate, cancellationToken);

                if (result == null)
                {
                    logger.LogDebug("Yahoo returned no data for {Ticker} on {Date}", yahooSymbol, targetDate);
                    continue;
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
                logger.LogDebug(ex, "Yahoo fetch failed for {Ticker}, will try fallback", yahooSymbol);
            }
        }

        return null;
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
    /// Build Yahoo Finance symbols for historical lookup.
    /// Taiwan historical lookup should try both listed (.TW) and OTC (.TWO) suffixes.
    /// </summary>
    private static IEnumerable<string> GetYahooSymbolsForHistoricalLookup(
        string ticker,
        StockMarket? market,
        bool isTaiwanStock)
    {
        if (isTaiwanStock)
        {
            var baseTicker = ticker.Split('.')[0];
            var explicitSuffix = ticker.Contains('.') ? ticker[(ticker.LastIndexOf('.') + 1)..] : null;

            if (string.Equals(explicitSuffix, "TW", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TW";
                yield return $"{baseTicker}.TWO";
                yield break;
            }

            if (string.Equals(explicitSuffix, "TWO", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TWO";
                yield return $"{baseTicker}.TW";
                yield break;
            }

            yield return $"{baseTicker}.TW";
            yield return $"{baseTicker}.TWO";
            yield break;
        }

        yield return YahooSymbolHelper.ConvertToYahooSymbol(ticker, market);
    }

    /// <summary>
    /// 判斷 ticker 是否為台股代號（包含數字開頭且可能帶字尾，例如 00632R）。
    /// </summary>
    private static bool IsTaiwanStock(string ticker)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalizedTicker))
        {
            return false;
        }

        var baseTicker = normalizedTicker.Split('.')[0];
        if (baseTicker.Length == 0)
        {
            return false;
        }

        // 與其他路徑一致：以數字開頭視為台股（例如 2330、0050、00632R）
        return char.IsDigit(baseTicker[0]);
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
            Source = result.Source,
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
                CurrencyPair = BuildCurrencyPairKey(fromCurrency, toCurrency),
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

    private static string BuildStockCacheTickerKey(string normalizedTicker, StockMarket? market)
    {
        if (market == null)
        {
            return normalizedTicker;
        }

        return $"{normalizedTicker}|{market.Value}";
    }

    private static IEnumerable<string> GetManualStockCacheTickersToCheck(string normalizedTicker)
    {
        yield return normalizedTicker;

        foreach (var market in Enum.GetValues<StockMarket>())
        {
            yield return BuildStockCacheTickerKey(normalizedTicker, market);
        }
    }

    private static string NormalizeCurrencyCode(string currency)
    {
        return currency.Trim().ToUpperInvariant();
    }

    private static string BuildCurrencyPairKey(string fromCurrency, string toCurrency)
    {
        return $"{NormalizeCurrencyCode(fromCurrency)}:{NormalizeCurrencyCode(toCurrency)}";
    }
}
