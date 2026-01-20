using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 取得市場指數／ETF 價格以進行 CAPE 即時調整的服務。
/// - 美股／全球市場：Sina 取即時價、Stooq 取歷史價
/// - 台灣：TWSE 同時提供即時與歷史價格
/// 若外部來源不可用，歷史價格會回退使用資料庫快取。
/// </summary>
public class IndexPriceService(
    ISinaEtfPriceService sinaEtfPriceService,
    IStooqHistoricalPriceService stooqHistoricalPriceService,
    ITwseIndexPriceService twseIndexPriceService,
    AppDbContext dbContext,
    ILogger<IndexPriceService> logger) : IIndexPriceService
{
    // CAPE 調整所支援的所有市場（marketKey）
    public static readonly IReadOnlyCollection<string> SupportedMarkets =
    [
        "All Country",              // VWRA - Vanguard FTSE All-World
        "US Large",                 // VUAA - Vanguard S&P 500
        "US Small",                 // XRSU - Xtrackers Russell 2000
        "Taiwan",                   // TWII - Taiwan Weighted Index
        "Emerging Markets",         // VFEM - Vanguard FTSE Emerging Markets
        "Europe",                   // VEUA - Vanguard FTSE Developed Europe
        "Japan",                    // VJPA - Vanguard FTSE Japan
        "China",                    // HCHA - HSBC MSCI China
        "Developed Markets Large",  // VHVE - Vanguard FTSE Developed World
        "Developed Markets Small",  // WSML - iShares MSCI World Small Cap
        "Dev ex US Large",          // EXUS - Vanguard FTSE Developed ex US
    ];

    public async Task<IndexPriceData?> GetIndexPricesAsync(
        string marketKey,
        DateTime referenceDate,
        CancellationToken cancellationToken = default)
    {
        // 確認 marketKey 是否受支援
        if (!SupportedMarkets.Contains(marketKey))
        {
            logger.LogDebug("Market {Market} is not supported for CAPE adjustment", marketKey);
            return null;
        }

        try
        {
            decimal? currentPrice;
            decimal? referencePrice;
            var referenceYearMonth = GetReferenceYearMonth(referenceDate);

            if (marketKey == "Taiwan")
            {
                // 台灣同時使用 TWSE 取得即時與歷史價格
                // 使用 fallback 版本：即時價失敗時會嘗試改抓歷史資料
                currentPrice = await twseIndexPriceService.GetCurrentPriceWithFallbackAsync(cancellationToken);
                referencePrice = await GetTaiwanReferencePriceAsync(referenceDate, referenceYearMonth, cancellationToken);
            }
            else
            {
                // 美股／全球市場使用 Sina + Stooq
                currentPrice = await sinaEtfPriceService.GetCurrentPriceAsync(marketKey, cancellationToken);
                referencePrice = await GetReferencePriceAsync(marketKey, referenceDate, referenceYearMonth, cancellationToken);
            }

            if (currentPrice == null)
            {
                logger.LogWarning("Failed to get current price for {Market}", marketKey);
                return null;
            }

            if (referencePrice == null)
            {
                logger.LogWarning(
                    "No reference price found for {Market} at {YearMonth}.",
                    marketKey, referenceYearMonth);
                return null;
            }

            logger.LogDebug(
                "Got prices for {Market}: current={Current}, reference={Reference} ({YearMonth})",
                marketKey, currentPrice, referencePrice, referenceYearMonth);

            return new IndexPriceData(
                marketKey,
                marketKey,
                currentPrice.Value,
                referencePrice.Value,
                referenceDate,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching index prices for {Market}", marketKey);
            return null;
        }
    }

    private static string GetReferenceYearMonth(DateTime referenceDate)
    {
        return $"{referenceDate.Year}{referenceDate.Month:D2}";
    }

    /// <summary>
    /// 取得台灣市場的基準價格：優先嘗試 TWSE，失敗則回退使用資料庫。
    /// </summary>
    private async Task<decimal?> GetTaiwanReferencePriceAsync(
        DateTime referenceDate,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        // 先查資料庫是否已有快取／人工補登資料
        var dbPrice = await GetDatabasePriceAsync("Taiwan", yearMonth, cancellationToken);
        if (dbPrice != null)
        {
            return dbPrice;
        }

        // 嘗試從 TWSE 抓取
        var twsePrice = await twseIndexPriceService.GetMonthEndPriceAsync(
            referenceDate.Year,
            referenceDate.Month,
            cancellationToken);

        if (twsePrice != null)
        {
            // 寫入資料庫快取，避免下次重複抓取
            await SavePriceToDatabase("Taiwan", yearMonth, twsePrice.Value, cancellationToken);
            return twsePrice;
        }

        return null;
    }

    /// <summary>
    /// 取得美股／全球市場的基準價格：優先嘗試 Stooq，失敗則回退使用資料庫。
    /// 實作 negative caching：若確認價格不可取得，會存入 NotAvailable 標記以避免重複呼叫外部 API。
    /// </summary>
    private async Task<decimal?> GetReferencePriceAsync(
        string marketKey,
        DateTime referenceDate,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        // 先查資料庫是否已有快取（包含 NotAvailable 標記）
        var snapshot = await dbContext.IndexPriceSnapshots
            .FirstOrDefaultAsync(
                s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                cancellationToken);

        if (snapshot != null)
        {
            // 若標記為 NotAvailable，直接回傳 null（略過外部 API 呼叫）
            if (snapshot.IsNotAvailable)
            {
                logger.LogDebug("Negative cache hit for {Market}/{YearMonth} - marked NotAvailable", marketKey, yearMonth);
                return null;
            }
            return snapshot.Price;
        }

        // 嘗試從 Stooq 抓取
        var stooqPrice = await stooqHistoricalPriceService.GetMonthEndPriceAsync(
            marketKey,
            referenceDate.Year,
            referenceDate.Month,
            cancellationToken);

        if (stooqPrice != null)
        {
            // 寫入資料庫快取，避免下次重複抓取
            await SavePriceToDatabase(marketKey, yearMonth, stooqPrice.Value, cancellationToken);
            return stooqPrice;
        }

        // 外部 API 回傳 null：儲存 NotAvailable 標記（negative caching）
        // 僅針對歷史月份（非當月，避免當月資料尚未更新導致誤判）
        var now = DateTime.UtcNow;
        var currentYearMonth = $"{now.Year}{now.Month:D2}";
        if (yearMonth != currentYearMonth)
        {
            await SaveNotAvailableMarker(marketKey, yearMonth, cancellationToken);
        }

        return null;
    }

    private async Task<decimal?> GetDatabasePriceAsync(
        string marketKey,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.IndexPriceSnapshots
            .FirstOrDefaultAsync(
                s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                cancellationToken);

        return snapshot?.Price;
    }

    private async Task SavePriceToDatabase(
        string marketKey,
        string yearMonth,
        decimal price,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await dbContext.IndexPriceSnapshots
                .FirstOrDefaultAsync(
                    s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                    cancellationToken);

            if (existing != null)
            {
                existing.Price = price;
                existing.RecordedAt = DateTime.UtcNow;
            }
            else
            {
                dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                {
                    MarketKey = marketKey,
                    YearMonth = yearMonth,
                    Price = price,
                    RecordedAt = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cached reference price {Price} for {Market} {YearMonth}", price, marketKey, yearMonth);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // 可能已被其他並發請求寫入；可忽略
            logger.LogDebug("Duplicate key ignored for {Market} {YearMonth} - already exists", marketKey, yearMonth);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cache reference price for {Market} {YearMonth}", marketKey, yearMonth);
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation: 23505
        // SQLite constraint violation: 19 (SQLITE_CONSTRAINT)
        // 以錯誤訊息字串判斷；若底層 provider 變更，可能需要更新此判斷邏輯
        return ex.InnerException?.Message.Contains("23505") == true ||
               ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true;
    }

    /// <summary>
    /// 儲存指定 (MarketKey, YearMonth) 的 NotAvailable 標記。
    /// 這可以避免對已確認不可取得的價格重複呼叫外部 API。
    /// </summary>
    private async Task SaveNotAvailableMarker(
        string marketKey,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await dbContext.IndexPriceSnapshots
                .FirstOrDefaultAsync(
                    s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                    cancellationToken);

            if (existing != null)
            {
                // 已存在：避免用 NotAvailable 覆蓋有效價格
                return;
            }

            dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
            {
                MarketKey = marketKey,
                YearMonth = yearMonth,
                Price = null,
                IsNotAvailable = true,
                RecordedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Saved NotAvailable marker for {Market}/{YearMonth}", marketKey, yearMonth);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            logger.LogDebug("Duplicate key ignored for NotAvailable marker {Market}/{YearMonth}", marketKey, yearMonth);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save NotAvailable marker for {Market}/{YearMonth}", marketKey, yearMonth);
        }
    }
}
