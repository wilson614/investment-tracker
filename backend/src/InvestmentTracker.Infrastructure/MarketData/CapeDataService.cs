using System.Net.Http.Json;
using System.Text.Json;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Research Affiliates API 取得 CAPE 資料的服務。
/// 會將資料存入資料庫；僅在當月資料可用時才會抓取較新的資料。
/// </summary>
public class CapeDataService(
    HttpClient httpClient,
    AppDbContext dbContext,
    IIndexPriceService indexPriceService,
    ILogger<CapeDataService> logger) : ICapeDataService
{
    private const string BaseUrl = "https://interactive.researchaffiliates.com/asset-allocation-data";

    public async Task<CapeDataResponse?> GetCapeDataAsync(CancellationToken cancellationToken = default)
    {
        // 從資料庫取得最新快照
        var snapshot = await dbContext.CapeDataSnapshots
            .OrderByDescending(s => s.DataDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot != null)
        {
            // 解析資料日期，判斷是否需要檢查新資料
            var shouldCheckForNew = ShouldCheckForNewData(snapshot.DataDate, snapshot.FetchedAt);

            if (shouldCheckForNew)
            {
                // 嘗試取得較新的資料；若抓取失敗則仍回傳既有資料
                var newData = await TryFetchNewerDataAsync(snapshot.DataDate, cancellationToken);
                if (newData != null)
                {
                    return await ApplyRealTimeAdjustmentsAsync(newData, cancellationToken);
                }
            }

            var data = DeserializeSnapshot(snapshot);
            return await ApplyRealTimeAdjustmentsAsync(data, cancellationToken);
        }

        // 資料庫沒有資料，直接抓取最新資料
        var freshData = await FetchAndSaveAsync(cancellationToken);
        if (freshData != null)
        {
            return await ApplyRealTimeAdjustmentsAsync(freshData, cancellationToken);
        }
        return null;
    }

    /// <summary>
    /// 依照目前指數價格，對 CAPE 值套用即時調整。
    /// </summary>
    private async Task<CapeDataResponse> ApplyRealTimeAdjustmentsAsync(
        CapeDataResponse data,
        CancellationToken cancellationToken)
    {
        // 解析資料日期，取得歷史參考價格用的 reference date
        // API 日期格式："2026-01-02" 代表資料是以前一個月月底（2025-12-31）為準
        if (!DateTime.TryParse(data.Date, out var apiDate))
        {
            return data;
        }

        // CAPE 資料對應到前一個月月底
        var referenceDate = new DateTime(apiDate.Year, apiDate.Month, 1).AddDays(-1);

        var adjustedItems = new List<CapeDataItem>();

        foreach (var item in data.Items)
        {
            var adjustedValue = await CalculateAdjustedValueAsync(
                item.BoxName,
                item.CurrentValue,
                referenceDate,
                cancellationToken);

            adjustedItems.Add(item with { AdjustedValue = adjustedValue });
        }

        return data with { Items = adjustedItems };
    }

    /// <summary>
    /// 依照目前指數價與參考指數價，計算調整後的 CAPE。
    /// </summary>
    private async Task<decimal?> CalculateAdjustedValueAsync(
        string marketKey,
        decimal originalValue,
        DateTime referenceDate,
        CancellationToken cancellationToken)
    {
        // 確認此 marketKey 是否支援調整
        if (!IndexPriceService.SupportedMarkets.Contains(marketKey))
        {
            return null;
        }

        try
        {
            var indexPrices = await indexPriceService.GetIndexPricesAsync(
                marketKey,
                referenceDate,
                cancellationToken);

            if (indexPrices == null || indexPrices.CurrentPrice == 0 || indexPrices.ReferencePrice == 0)
            {
                logger.LogDebug("Could not get index prices for {Market}", marketKey);
                return null;
            }

            // 調整後 CAPE = 原始 CAPE ×（目前指數／參考指數）
            var adjustmentRatio = indexPrices.CurrentPrice / indexPrices.ReferencePrice;
            var adjustedValue = originalValue * adjustmentRatio;

            logger.LogDebug(
                "Adjusted {Market} CAPE: {Original} × ({Current}/{Reference}) = {Adjusted}",
                marketKey, originalValue, indexPrices.CurrentPrice, indexPrices.ReferencePrice, adjustedValue);

            return Math.Round(adjustedValue, 2);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to calculate adjusted CAPE for {Market}", marketKey);
            return null;
        }
    }

    /// <summary>
    /// 判斷是否需要檢查新的 CAPE 資料。
    /// - 若已有當月資料：不需檢查
    /// - 若仍為上月資料：每天最多檢查一次
    /// </summary>
    private static bool ShouldCheckForNewData(string dataDate, DateTime lastFetchedAt)
    {
        var now = DateTime.UtcNow;
        var currentYearMonth = $"{now.Year}-{now.Month:D2}";

        // 從 data date 抽出 year-month（格式："2026-01-02"）
        var dataYearMonth = dataDate.Substring(0, 7);

        // 若已是當月資料，就不需要檢查
        if (dataYearMonth == currentYearMonth)
        {
            return false;
        }

        // 資料為上月：每天最多檢查一次
        return (now - lastFetchedAt) > TimeSpan.FromDays(1);
    }

    public async Task<CapeDataResponse?> RefreshCapeDataAsync(CancellationToken cancellationToken = default)
    {
        var data = await FetchAndSaveAsync(cancellationToken);
        if (data != null)
        {
            return await ApplyRealTimeAdjustmentsAsync(data, cancellationToken);
        }
        return null;
    }

    private async Task<CapeDataResponse?> TryFetchNewerDataAsync(string currentDataDate, CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null) return null;

        // 只有在找到更新的資料時才寫入
        if (string.Compare(result.Date, currentDataDate, StringComparison.Ordinal) > 0)
        {
            await SaveSnapshotAsync(result, cancellationToken);
            return result;
        }

        // 更新 FetchedAt，避免短時間內重複檢查
        var snapshot = await dbContext.CapeDataSnapshots
            .FirstOrDefaultAsync(s => s.DataDate == currentDataDate, cancellationToken);
        if (snapshot != null)
        {
            snapshot.FetchedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private async Task<CapeDataResponse?> FetchAndSaveAsync(CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null)
        {
            logger.LogWarning("Failed to discover CAPE data"); // 無法探索到 CAPE 資料
            return null;
        }

        await SaveSnapshotAsync(result, cancellationToken);
        return result;
    }

    private async Task SaveSnapshotAsync(CapeDataResponse data, CancellationToken cancellationToken)
    {
        try
        {
            // 確認是否已存在同日期快照
            var existing = await dbContext.CapeDataSnapshots
                .FirstOrDefaultAsync(s => s.DataDate == data.Date, cancellationToken);

            // 儲存不含 AdjustedValue 的 items（AdjustedValue 會即時計算）
            var itemsToStore = data.Items.Select(i => new StoredCapeItem(
                i.BoxName, i.CurrentValue, i.CurrentValuePercentile,
                i.Range25th, i.Range50th, i.Range75th
            )).ToList();

            if (existing != null)
            {
                existing.ItemsJson = JsonSerializer.Serialize(itemsToStore);
                existing.FetchedAt = DateTime.UtcNow;
            }
            else
            {
                var snapshot = new CapeDataSnapshot
                {
                    DataDate = data.Date,
                    ItemsJson = JsonSerializer.Serialize(itemsToStore),
                    FetchedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.CapeDataSnapshots.Add(snapshot);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Saved CAPE data snapshot for date {Date}", data.Date); // 已儲存 CAPE 快照
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // 可能已被其他並發請求寫入；可忽略
            logger.LogDebug("Duplicate CAPE snapshot ignored for {Date} - already exists", data.Date);
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation: 23505
        // SQLite constraint violation: 19 (SQLITE_CONSTRAINT)
        // 以錯誤訊息字串判斷；若底層 provider 變更，可能需要更新此判斷邏輯
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true;
    }

    private static CapeDataResponse DeserializeSnapshot(CapeDataSnapshot snapshot)
    {
        var storedItems = JsonSerializer.Deserialize<List<StoredCapeItem>>(snapshot.ItemsJson) ?? [];
        var items = storedItems.Select(i => new CapeDataItem(
            i.BoxName, i.CurrentValue, null, i.CurrentValuePercentile,
            i.Range25th, i.Range50th, i.Range75th
        )).ToList();
        return new CapeDataResponse(snapshot.DataDate, items, snapshot.FetchedAt);
    }

    /// <summary>
    /// 探索最新可用的 CAPE 資料。
    /// 會先嘗試當月（1～10 日），再嘗試上月。
    /// </summary>
    private async Task<CapeDataResponse?> DiscoverLatestCapeDataAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var year = now.Year;
        var month = now.Month;

        // 僅嘗試兩個月份：當月與上月
        for (var monthIndex = 0; monthIndex < 2; monthIndex++)
        {
            // 依序嘗試 1～10 日
            for (var day = 1; day <= 10; day++)
            {
                var items = await TryFetchForDateAsync(year, month, day, cancellationToken);
                if (items != null)
                {
                    var date = $"{year}-{month:D2}-{day:D2}";
                    logger.LogInformation("Found CAPE data for {Date}", date); // 找到可用的 CAPE 資料
                    return new CapeDataResponse(date, items, DateTime.UtcNow);
                }
            }

            // 切換到上月
            month--;
            if (month == 0)
            {
                month = 12;
                year--;
            }
        }

        return null;
    }

    private async Task<List<CapeDataItem>?> TryFetchForDateAsync(
        int year, int month, int day, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/{year}{month:D2}/{day:D2}/boxplot/boxplot_shillerpe.json";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var rawItems = await response.Content.ReadFromJsonAsync<List<RawCapeItem>>(jsonOptions, cancellationToken);
            if (rawItems == null || rawItems.Count == 0)
            {
                return null;
            }

            return rawItems.Select(item => new CapeDataItem(
                item.BoxName ?? "",
                item.CurrentValue,
                null, // AdjustedValue 會在後續即時計算
                item.CurrentValuePercentile,
                item.Range25th,
                item.Range50th,
                item.Range75th
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch CAPE data for {Year}-{Month:D2}-{Day:D2}", year, month, day);
            return null;
        }
    }

    private record RawCapeItem(
        string? BoxName,
        decimal CurrentValue,
        decimal CurrentValuePercentile,
        decimal Range25th,
        decimal Range50th,
        decimal Range75th
    );

    // 用於資料庫儲存（不含 AdjustedValue）
    private record StoredCapeItem(
        string BoxName,
        decimal CurrentValue,
        decimal CurrentValuePercentile,
        decimal Range25th,
        decimal Range50th,
        decimal Range75th
    );
}
