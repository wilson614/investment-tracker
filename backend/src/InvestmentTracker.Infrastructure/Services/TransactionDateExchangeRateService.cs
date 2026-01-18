using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Service for managing historical exchange rate cache by transaction date.
/// Provides lazy loading: check cache first, fetch from Stooq if missing, save to cache.
/// </summary>
public class TransactionDateExchangeRateService : ITransactionDateExchangeRateService
{
    private readonly IHistoricalExchangeRateCacheRepository _repository;
    private readonly IStooqHistoricalPriceService _stooqService;
    private readonly ILogger<TransactionDateExchangeRateService> _logger;

    public TransactionDateExchangeRateService(
        IHistoricalExchangeRateCacheRepository repository,
        IStooqHistoricalPriceService stooqService,
        ILogger<TransactionDateExchangeRateService> logger)
    {
        _repository = repository;
        _stooqService = stooqService;
        _logger = logger;
    }

    public async Task<TransactionDateExchangeRateResult?> GetOrFetchAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        var dateOnly = transactionDate.Date;

        // Check cache first
        var cached = await _repository.GetAsync(currencyPair, dateOnly, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for {CurrencyPair}/{Date}: {Rate}", 
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

        // Fetch from Stooq API
        _logger.LogInformation("Cache miss for {CurrencyPair}/{Date}, fetching from Stooq...", 
            currencyPair, dateOnly.ToString("yyyy-MM-dd"));
        
        var apiResult = await FetchFromStooqAsync(fromCurrency, toCurrency, dateOnly, cancellationToken);

        if (apiResult != null)
        {
            // Save to cache
            try
            {
                var cacheEntry = HistoricalExchangeRateCache.Create(
                    fromCurrency,
                    toCurrency,
                    dateOnly,
                    apiResult.Rate,
                    apiResult.ActualDate,
                    apiResult.Source);

                await _repository.AddAsync(cacheEntry, cancellationToken);
                apiResult.FromCache = false;
                _logger.LogInformation("Cached exchange rate for {CurrencyPair}/{Date}: {Rate}",
                    currencyPair, dateOnly.ToString("yyyy-MM-dd"), apiResult.Rate);
            }
            catch (InvalidOperationException ex)
            {
                // Entry might have been added by another request - this is fine
                _logger.LogDebug(ex, "Cache entry already exists for {CurrencyPair}/{Date}", 
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

        // Check if entry already exists
        if (await _repository.ExistsAsync(currencyPair, dateOnly, cancellationToken))
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

        await _repository.AddAsync(cacheEntry, cancellationToken);
        _logger.LogInformation("Manually cached exchange rate for {CurrencyPair}/{Date}: {Rate}",
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
        return await _repository.ExistsAsync(currencyPair, transactionDate.Date, cancellationToken);
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
            var result = await _stooqService.GetExchangeRateAsync(
                fromCurrency, toCurrency, targetDate, cancellationToken);

            if (result == null)
            {
                _logger.LogWarning("Could not fetch exchange rate for {From}/{To}/{Date} from Stooq",
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
            _logger.LogWarning(ex, "Error fetching exchange rate for {From}/{To}/{Date}",
                fromCurrency, toCurrency, transactionDate.ToString("yyyy-MM-dd"));
            return null;
        }
    }
}
