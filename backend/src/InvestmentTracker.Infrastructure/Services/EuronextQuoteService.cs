using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 取得並快取 Euronext 報價的服務。
/// 內含報價快取與匯率換算邏輯。
/// </summary>
public class EuronextQuoteService(
    IEuronextApiClient apiClient,
    IEuronextQuoteCacheRepository cacheRepository,
    IExchangeRateProvider exchangeRateProvider,
    ILogger<EuronextQuoteService> logger)
{
    private readonly IEuronextApiClient _apiClient = apiClient;
    private readonly IEuronextQuoteCacheRepository _cacheRepository = cacheRepository;
    private readonly IExchangeRateProvider _exchangeRateProvider = exchangeRateProvider;
    private readonly ILogger<EuronextQuoteService> _logger = logger;

    // 快取有效期間（分鐘）（交易時段預設 15 分鐘）
    private const int CacheMinutes = 15;

    /// <summary>
    /// 取得 Euronext 掛牌股票報價，並可選擇換算成指定的本位幣匯率。
    /// </summary>
    /// <param name="isin">ISIN（例如：AGAC 的 IE000FHBZDZ8）</param>
    /// <param name="mic">Market Identifier Code（例如：阿姆斯特丹 XAMS）</param>
    /// <param name="homeCurrency">要換算的目標幣別（預設：TWD）</param>
    /// <param name="forceRefresh">是否略過快取直接刷新</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>包含價格與匯率的報價結果</returns>
    public async Task<EuronextQuoteResult?> GetQuoteAsync(
        string isin,
        string mic,
        string homeCurrency = "TWD",
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 優先嘗試從快取取得
            if (!forceRefresh)
            {
                var cached = await _cacheRepository.GetByIsinAndMicAsync(isin, mic, cancellationToken);
                if (cached != null && !cached.IsStale && !IsCacheExpired(cached))
                {
                    _logger.LogDebug("Using cached quote for {Isin}-{Mic}", isin, mic);

                    // 取得快取資料對應的匯率
                    var rate = await GetExchangeRateAsync(cached.Currency, homeCurrency, cancellationToken);

                    return new EuronextQuoteResult(
                        cached.Price,
                        cached.Currency,
                        cached.MarketTime,
                        cached.Isin,
                        rate,
                        true,
                        cached.ChangePercent,
                        cached.Change);
                }
            }

            // 抓取最新報價
            _logger.LogInformation("Fetching fresh quote for {Isin}-{Mic}", isin, mic);
            var quote = await _apiClient.GetQuoteAsync(isin, mic, cancellationToken);

            if (quote == null)
            {
                _logger.LogWarning("No quote returned for {Isin}-{Mic}", isin, mic);
                return null;
            }

            // 寫入快取
            var cacheEntry = new EuronextQuoteCache(
                isin,
                mic,
                quote.Price,
                quote.Currency,
                quote.MarketTime ?? DateTime.UtcNow,
                quote.ChangePercent,
                quote.Change);

            await _cacheRepository.UpsertAsync(cacheEntry, cancellationToken);

            // 取得匯率
            var exchangeRate = await GetExchangeRateAsync(quote.Currency, homeCurrency, cancellationToken);

            return new EuronextQuoteResult(
                quote.Price,
                quote.Currency,
                quote.MarketTime,
                quote.Name,
                exchangeRate,
                false,
                quote.ChangePercent,
                quote.Change);
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
/// 含匯率的 Euronext 報價結果。
/// </summary>
public record EuronextQuoteResult(
    decimal Price,
    string Currency,
    DateTime? MarketTime,
    string? Name,
    decimal? ExchangeRate,
    bool FromCache,
    string? ChangePercent = null,
    decimal? Change = null);
