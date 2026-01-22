using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 取得 Euronext 報價的服務。
/// 使用 SymbolMapping 快取 ticker → ISIN/MIC 對應，報價本身不快取到 DB。
/// </summary>
public class EuronextQuoteService(
    IEuronextApiClient apiClient,
    IEuronextSymbolMappingRepository symbolMappingRepository,
    IExchangeRateProvider exchangeRateProvider,
    ILogger<EuronextQuoteService> logger)
{
    /// <summary>
    /// 依 ticker 取得 Euronext 報價。
    /// 會自動查詢並快取 ticker → ISIN/MIC 對應。
    /// </summary>
    /// <param name="ticker">股票代碼（如 AGAC、SSAC）</param>
    /// <param name="homeCurrency">要換算的目標幣別（預設：TWD）</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>包含價格與匯率的報價結果</returns>
    public async Task<EuronextQuoteResult?> GetQuoteByTickerAsync(
        string ticker,
        string homeCurrency = "TWD",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 查詢 ticker → ISIN/MIC 對應
            var mapping = await GetOrCreateMappingAsync(ticker, cancellationToken);
            if (mapping == null)
            {
                logger.LogWarning("Cannot find ISIN/MIC mapping for ticker {Ticker}", ticker);
                return null;
            }

            // 2. 使用 ISIN/MIC 取得報價
            return await GetQuoteAsync(mapping.Isin, mapping.Mic, homeCurrency, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting quote for ticker {Ticker}", ticker);
            return null;
        }
    }

    /// <summary>
    /// 取得 Euronext 掛牌股票報價，並可選擇換算成指定的本位幣匯率。
    /// </summary>
    /// <param name="isin">ISIN（例如：AGAC 的 IE000FHBZDZ8）</param>
    /// <param name="mic">Market Identifier Code（例如：阿姆斯特丹 XAMS）</param>
    /// <param name="homeCurrency">要換算的目標幣別（預設：TWD）</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>包含價格與匯率的報價結果</returns>
    public async Task<EuronextQuoteResult?> GetQuoteAsync(
        string isin,
        string mic,
        string homeCurrency = "TWD",
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Fetching Euronext quote for {Isin}-{Mic}", isin, mic);
            var quote = await apiClient.GetQuoteAsync(isin, mic, cancellationToken);

            if (quote == null)
            {
                logger.LogWarning("No quote returned for {Isin}-{Mic}", isin, mic);
                return null;
            }

            // 取得匯率
            var exchangeRate = await GetExchangeRateAsync(quote.Currency, homeCurrency, cancellationToken);

            return new EuronextQuoteResult(
                quote.Price,
                quote.Currency,
                quote.MarketTime,
                quote.Name,
                exchangeRate,
                false, // 不再從 DB 快取取得
                quote.ChangePercent,
                quote.Change);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting quote for {Isin}-{Mic}", isin, mic);
            return null;
        }
    }

    /// <summary>
    /// 取得或建立 ticker → ISIN/MIC 對應。
    /// 如果 DB 中沒有，會呼叫 Euronext Search API 查詢並快取。
    /// </summary>
    private async Task<EuronextSymbolMapping?> GetOrCreateMappingAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        // 先從 DB 查詢
        var existing = await symbolMappingRepository.GetByTickerAsync(ticker, cancellationToken);
        if (existing != null)
        {
            logger.LogDebug("Found existing mapping for {Ticker}: {Isin}-{Mic}",
                ticker, existing.Isin, existing.Mic);
            return existing;
        }

        // 呼叫 Euronext Search API
        logger.LogInformation("Searching Euronext for ticker {Ticker}", ticker);
        var searchResults = await apiClient.SearchAsync(ticker, cancellationToken);

        if (searchResults.Count == 0)
        {
            logger.LogWarning("No search results for ticker {Ticker}", ticker);
            return null;
        }

        // 取第一個完全匹配的結果
        var result = searchResults[0];

        // 快取到 DB
        var mapping = new EuronextSymbolMapping(
            result.Ticker,
            result.Isin,
            result.Mic,
            result.Currency,
            result.Name);

        await symbolMappingRepository.UpsertAsync(mapping, cancellationToken);
        logger.LogInformation("Cached mapping for {Ticker}: {Isin}-{Mic}",
            ticker, result.Isin, result.Mic);

        return mapping;
    }

    /// <summary>
    /// 取得 ticker 對應的 ISIN/MIC 資訊。
    /// 供 API endpoint 使用。
    /// </summary>
    public async Task<EuronextSymbolMapping?> GetSymbolMappingAsync(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        return await GetOrCreateMappingAsync(ticker, cancellationToken);
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
            var response = await exchangeRateProvider.GetExchangeRateAsync(fromCurrency, toCurrency, cancellationToken);
            return response?.Rate;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get exchange rate from {From} to {To}", fromCurrency, toCurrency);
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
