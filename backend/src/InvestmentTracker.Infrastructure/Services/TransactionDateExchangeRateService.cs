using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 依交易日期管理歷史匯率快取的服務。
/// 採 lazy loading：先查快取，若缺少則從 Yahoo Finance 或 Stooq 抓取並寫入快取。
/// 優先使用 Yahoo Finance，若失敗則 fallback 到 Stooq。
/// </summary>
public class TransactionDateExchangeRateService(
    IHistoricalExchangeRateCacheRepository repository,
    IYahooHistoricalPriceService yahooService,
    IStooqHistoricalPriceService stooqService,
    ILogger<TransactionDateExchangeRateService> logger) : ITransactionDateExchangeRateService
{
    public async Task<TransactionDateExchangeRateResult?> GetOrFetchAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        // 確保日期為 UTC Kind，以相容 PostgreSQL
        var dateOnly = DateTime.SpecifyKind(transactionDate.Date, DateTimeKind.Utc);

        // 先查快取
        var cached = await repository.GetAsync(currencyPair, dateOnly, cancellationToken);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for {CurrencyPair}/{Date}: {Rate}",
                currencyPair, dateOnly.ToString("yyyy-MM-dd"), cached.Rate);
            return new TransactionDateExchangeRateResult
            {
                Rate = cached.Rate,
                CurrencyPair = cached.CurrencyPair,
                RequestedDate = cached.RequestedDate,
                ActualDate = cached.ActualDate,
                Source = cached.Source,
                FromCache = true
            };
        }

        // 優先從 Yahoo Finance 抓取
        logger.LogInformation("Cache miss for {CurrencyPair}/{Date}, fetching from Yahoo Finance...",
            currencyPair, dateOnly.ToString("yyyy-MM-dd"));

        var apiResult = await FetchFromYahooAsync(fromCurrency, toCurrency, dateOnly, cancellationToken);

        // Yahoo 失敗時 fallback 到 Stooq
        if (apiResult == null)
        {
            logger.LogInformation("Yahoo Finance failed for {CurrencyPair}/{Date}, falling back to Stooq...",
                currencyPair, dateOnly.ToString("yyyy-MM-dd"));
            apiResult = await FetchFromStooqAsync(fromCurrency, toCurrency, dateOnly, cancellationToken);
        }

        if (apiResult != null)
        {
            // 寫入快取
            try
            {
                var cacheEntry = HistoricalExchangeRateCache.Create(
                    fromCurrency,
                    toCurrency,
                    dateOnly,
                    apiResult.Rate,
                    apiResult.ActualDate,
                    apiResult.Source);

                await repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                logger.LogInformation("Cached exchange rate for {CurrencyPair}/{Date}: {Rate}",
                    currencyPair, dateOnly.ToString("yyyy-MM-dd"), apiResult.Rate);
            }
            catch (InvalidOperationException ex)
            {
                // 可能已被其他並發請求寫入；可忽略
                logger.LogDebug(ex, "Cache entry already exists for {CurrencyPair}/{Date}", 
                    currencyPair, dateOnly.ToString("yyyy-MM-dd"));
            }
        }

        return apiResult;
    }

    public async Task<TransactionDateExchangeRateResult> SaveManualAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        decimal rate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        var dateOnly = transactionDate.Date;

        // 確認是否已存在快取資料
        if (await repository.ExistsAsync(currencyPair, dateOnly, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {currencyPair}/{dateOnly:yyyy-MM-dd}. " +
                "Manual entry only allowed for empty cache entries.");
        }

        var cacheEntry = HistoricalExchangeRateCache.CreateManual(
            fromCurrency,
            toCurrency,
            dateOnly,
            rate);

        await repository.AddAsync(cacheEntry, cancellationToken);
        logger.LogInformation("Manually cached exchange rate for {CurrencyPair}/{Date}: {Rate}",
            currencyPair, dateOnly.ToString("yyyy-MM-dd"), rate);

        return new TransactionDateExchangeRateResult
        {
            Rate = rate,
            CurrencyPair = currencyPair,
            RequestedDate = dateOnly,
            ActualDate = dateOnly,
            Source = "Manual",
            FromCache = true
        };
    }

    public async Task<bool> ExistsAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        return await repository.ExistsAsync(currencyPair, transactionDate.Date, cancellationToken);
    }

    private async Task<TransactionDateExchangeRateResult?> FetchFromYahooAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = DateOnly.FromDateTime(transactionDate);
            var result = await yahooService.GetExchangeRateAsync(
                fromCurrency, toCurrency, targetDate, cancellationToken);

            if (result == null)
            {
                logger.LogWarning("Could not fetch exchange rate for {From}/{To}/{Date} from Yahoo Finance",
                    fromCurrency, toCurrency, transactionDate.ToString("yyyy-MM-dd"));
                return null;
            }

            return new TransactionDateExchangeRateResult
            {
                Rate = result.Rate,
                CurrencyPair = result.CurrencyPair,
                RequestedDate = transactionDate.Date,
                ActualDate = result.ActualDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Source = "Yahoo",
                FromCache = false
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching exchange rate for {From}/{To}/{Date} from Yahoo Finance",
                fromCurrency, toCurrency, transactionDate.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    private async Task<TransactionDateExchangeRateResult?> FetchFromStooqAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = DateOnly.FromDateTime(transactionDate);
            var result = await stooqService.GetExchangeRateAsync(
                fromCurrency, toCurrency, targetDate, cancellationToken);

            if (result == null)
            {
                logger.LogWarning("Could not fetch exchange rate for {From}/{To}/{Date} from Stooq",
                    fromCurrency, toCurrency, transactionDate.ToString("yyyy-MM-dd"));
                return null;
            }

            return new TransactionDateExchangeRateResult
            {
                Rate = result.Rate,
                CurrencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}",
                RequestedDate = transactionDate.Date,
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
            logger.LogWarning(ex, "Error fetching exchange rate for {From}/{To}/{Date}",
                fromCurrency, toCurrency, transactionDate.ToString("yyyy-MM-dd"));
            return null;
        }
    }
}
