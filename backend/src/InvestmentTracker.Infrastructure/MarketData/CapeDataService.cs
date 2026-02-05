using System.Globalization;
using System.Linq;
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

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private record CapeDataFetchResult(
        CapeDataResponse Data,
        IReadOnlyDictionary<string, List<decimal>>? HistoricalCapesByMarket
    );

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
                    var adjusted = await ApplyRealTimeAdjustmentsAsync(
                        newData.Data,
                        newData.HistoricalCapesByMarket,
                        cancellationToken);
                    return adjusted.Data;
                }
            }

            var cachedData = DeserializeSnapshot(snapshot);
            var cachedAdjusted = await ApplyRealTimeAdjustmentsAsync(
                cachedData.Data,
                cachedData.HistoricalCapesByMarket,
                cancellationToken);
            return cachedAdjusted.Data;
        }

        // 資料庫沒有資料，直接抓取最新資料
        var freshData = await FetchAndSaveAsync(cancellationToken);
        if (freshData != null)
        {
            var freshAdjusted = await ApplyRealTimeAdjustmentsAsync(
                freshData.Data,
                freshData.HistoricalCapesByMarket,
                cancellationToken);
            return freshAdjusted.Data;
        }
        return null;
    }

    /// <summary>
    /// 依照目前指數價格，對 CAPE 值套用即時調整。
    /// </summary>
    private async Task<CapeDataFetchResult> ApplyRealTimeAdjustmentsAsync(
        CapeDataResponse data,
        IReadOnlyDictionary<string, List<decimal>>? historicalCapesByMarket,
        CancellationToken cancellationToken)
    {
        // 解析資料日期，取得歷史參考價格用的 reference date
        // API 日期格式："2026-01-02" 代表資料是以前一個月月底（2025-12-31）為準
        if (!DateTime.TryParse(data.Date, out var apiDate))
        {
            return new CapeDataFetchResult(data, historicalCapesByMarket);
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

            var updatedPercentile = RecalculatePercentileOrFallback(
                item.BoxName,
                adjustedValue,
                item.CurrentValuePercentile,
                historicalCapesByMarket);

            adjustedItems.Add(item with
            {
                AdjustedValue = adjustedValue,
                CurrentValuePercentile = updatedPercentile
            });
        }

        var updatedData = data with { Items = adjustedItems };
        return new CapeDataFetchResult(updatedData, historicalCapesByMarket);
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
    /// 以 AdjustedValue 在該市場歷史分佈中的 percentile 取代原始 percentile。
    /// 若歷史資料不可用，則回退使用原始 percentile（保持向後相容）。
    /// </summary>
    private decimal RecalculatePercentileOrFallback(
        string marketKey,
        decimal? adjustedValue,
        decimal originalPercentile,
        IReadOnlyDictionary<string, List<decimal>>? historicalCapesByMarket)
    {
        if (adjustedValue == null)
        {
            logger.LogDebug(
                "AdjustedValue is null for {Market}; keep original percentile {Percentile}",
                marketKey,
                originalPercentile);
            return originalPercentile;
        }

        if (historicalCapesByMarket == null)
        {
            logger.LogDebug(
                "Historical CAPE data is not available; keep original percentile {Percentile} for {Market}",
                originalPercentile,
                marketKey);
            return originalPercentile;
        }

        if (!historicalCapesByMarket.TryGetValue(marketKey, out var historicalCapes) || historicalCapes.Count == 0)
        {
            logger.LogDebug(
                "Historical CAPE data is missing/empty for {Market}; keep original percentile {Percentile}",
                marketKey,
                originalPercentile);
            return originalPercentile;
        }

        var percentile = CalculatePercentileRank(adjustedValue.Value, historicalCapes);

        logger.LogDebug(
            "Recalculated percentile for {Market}: adjusted CAPE {AdjustedValue} -> percentile {Percentile} (original {OriginalPercentile})",
            marketKey,
            adjustedValue.Value,
            percentile,
            originalPercentile);

        return percentile;
    }

    /// <summary>
    /// 計算某值在歷史分佈中的 percentile rank（0-1）。
    /// 以「<= value 的比例」作為百分位數，確保回傳範圍為 [0, 1]。
    /// </summary>
    private static decimal CalculatePercentileRank(decimal value, List<decimal> historicalValues)
    {
        // 過濾無效數值並排序
        var values = historicalValues
            .Where(v => v > 0)
            .OrderBy(v => v)
            .ToList();

        if (values.Count == 0)
        {
            return 0m;
        }

        // percentile rank = count(values <= value) / n
        var count = 0;
        foreach (var v in values)
        {
            if (v <= value) count++;
            else break;
        }

        var pct = (decimal)count / values.Count;

        // Keep the same numeric scale as the upstream API field.
        // The UI currently expects 0-1 (see frontend PercentileBar). Some upstream data sources may return 0-100.
        return pct switch
        {
            < 0m => 0m,
            > 1m => 1m,
            _ => pct
        };
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
        var dataYearMonth = dataDate[..7];

        // 若已是當月資料，就不需要檢查
        if (dataYearMonth == currentYearMonth)
        {
            return false;
        }

        // 資料為上月：每天最多檢查一次
        return now - lastFetchedAt > TimeSpan.FromDays(1);
    }

    public async Task<CapeDataResponse?> RefreshCapeDataAsync(CancellationToken cancellationToken = default)
    {
        var data = await FetchAndSaveAsync(cancellationToken);
        if (data != null)
        {
            var adjusted = await ApplyRealTimeAdjustmentsAsync(
                data.Data,
                data.HistoricalCapesByMarket,
                cancellationToken);
            return adjusted.Data;
        }
        return null;
    }

    private async Task<CapeDataFetchResult?> TryFetchNewerDataAsync(string currentDataDate, CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null) return null;

        // 只有在找到更新的資料時才寫入
        if (string.Compare(result.Data.Date, currentDataDate, StringComparison.Ordinal) > 0)
        {
            await SaveSnapshotAsync(result.Data, result.HistoricalCapesByMarket, cancellationToken);
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

    private async Task<CapeDataFetchResult?> FetchAndSaveAsync(CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null)
        {
            logger.LogWarning("Failed to discover CAPE data"); // 無法探索到 CAPE 資料
            return null;
        }

        await SaveSnapshotAsync(result.Data, result.HistoricalCapesByMarket, cancellationToken);
        return result;
    }

    private async Task SaveSnapshotAsync(
        CapeDataResponse data,
        IReadOnlyDictionary<string, List<decimal>>? historicalCapesByMarket,
        CancellationToken cancellationToken)
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

            // Optional: store historical CAPE series for percentile recalculation.
            // Keep backward compatibility: if no historical series is available, store null.
            Dictionary<string, List<decimal>>? historicalCapesToStore = null;
            if (historicalCapesByMarket != null && historicalCapesByMarket.Count > 0)
            {
                historicalCapesToStore = historicalCapesByMarket
                    .Where(kvp => kvp.Value.Count > 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            }

            var payload = JsonSerializer.Serialize(new StoredCapeSnapshot(itemsToStore, historicalCapesToStore));

            if (existing != null)
            {
                existing.ItemsJson = payload;
                existing.FetchedAt = DateTime.UtcNow;
            }
            else
            {
                var snapshot = new CapeDataSnapshot
                {
                    DataDate = data.Date,
                    ItemsJson = payload,
                    FetchedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.CapeDataSnapshots.Add(snapshot);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved CAPE data snapshot for date {Date} (historicalSeriesIncluded={HasHistorical})",
                data.Date,
                historicalCapesToStore != null); // 已儲存 CAPE 快照
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
        return ex.InnerException?.Message.Contains("23505") == true ||
               ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true;
    }

    private static CapeDataFetchResult DeserializeSnapshot(CapeDataSnapshot snapshot)
    {
        // Backward compatibility:
        // - Old format: ItemsJson is a JSON array of StoredCapeItem
        // - New format: ItemsJson is a StoredCapeSnapshot object containing Items + optional HistoricalCapesByMarket

        List<StoredCapeItem> storedItems;
        IReadOnlyDictionary<string, List<decimal>>? historicalCapesByMarket = null;

        try
        {
            var storedSnapshot = JsonSerializer.Deserialize<StoredCapeSnapshot>(snapshot.ItemsJson);
            if (storedSnapshot?.Items != null)
            {
                storedItems = storedSnapshot.Items;
                historicalCapesByMarket = storedSnapshot.HistoricalCapesByMarket;
            }
            else
            {
                storedItems = JsonSerializer.Deserialize<List<StoredCapeItem>>(snapshot.ItemsJson) ?? [];
            }
        }
        catch
        {
            storedItems = JsonSerializer.Deserialize<List<StoredCapeItem>>(snapshot.ItemsJson) ?? [];
        }

        var items = storedItems.Select(i => new CapeDataItem(
            i.BoxName, i.CurrentValue, null, i.CurrentValuePercentile,
            i.Range25th, i.Range50th, i.Range75th
        )).ToList();
        var data = new CapeDataResponse(snapshot.DataDate, items, snapshot.FetchedAt);

        return new CapeDataFetchResult(data, historicalCapesByMarket);
    }

    /// <summary>
    /// 探索最新可用的 CAPE 資料。
    /// 會先嘗試當月（1～10 日），再嘗試上月。
    /// </summary>
    private async Task<CapeDataFetchResult?> DiscoverLatestCapeDataAsync(CancellationToken cancellationToken)
    {
        // Note: Research Affiliates percentile is based on original monthly CAPE.
        // We will recalculate percentile after real-time adjustment using historical CAPE series when available.
        // If historical CAPE data cannot be fetched, we fall back to the original percentile for backward compatibility.

        var now = DateTime.UtcNow;
        var year = now.Year;
        var month = now.Month;

        // 僅嘗試兩個月份：當月與上月
        for (var monthIndex = 0; monthIndex < 2; monthIndex++)
        {
            // 依序嘗試 1～10 日
            for (var day = 1; day <= 10; day++)
            {
                var result = await TryFetchForDateAsync(year, month, day, cancellationToken);
                if (result != null)
                {
                    logger.LogInformation("Found CAPE data for {Date}", result.Data.Date); // 找到可用的 CAPE 資料

                    if (result.HistoricalCapesByMarket != null)
                    {
                        logger.LogDebug(
                            "Historical CAPE series available for {MarketCount} markets",
                            result.HistoricalCapesByMarket.Count);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Historical CAPE series is not available in API response; using original percentile as fallback");
                    }

                    return result;
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

    private async Task<CapeDataFetchResult?> TryFetchForDateAsync(
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

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            // Items may be either the root array, or nested under an "Items" property.
            var itemsElement = doc.RootElement;
            if (itemsElement.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(itemsElement, "Items", out var nestedItems) &&
                nestedItems.ValueKind == JsonValueKind.Array)
            {
                itemsElement = nestedItems;
            }

            var rawItems = itemsElement.Deserialize<List<RawCapeItem>>(CaseInsensitiveOptions);
            if (rawItems == null || rawItems.Count == 0)
            {
                return null;
            }

            var items = rawItems.Select(item => new CapeDataItem(
                item.BoxName ?? "",
                item.CurrentValue,
                null, // AdjustedValue 會在後續即時計算
                item.CurrentValuePercentile,
                item.Range25th,
                item.Range50th,
                item.Range75th
            )).ToList();

            var date = $"{year}-{month:D2}-{day:D2}";
            var data = new CapeDataResponse(date, items, DateTime.UtcNow);

            var merged = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

            if (TryExtractHistoricalCapesByMarket(doc.RootElement, out var rootHist))
            {
                foreach (var (k, v) in rootHist)
                {
                    merged[k] = v;
                }
            }

            if (TryExtractHistoricalCapesByItem(itemsElement, out var itemHist))
            {
                foreach (var (k, v) in itemHist)
                {
                    merged[k] = v;
                }
            }

            IReadOnlyDictionary<string, List<decimal>>? historicalCapesByMarket = merged.Count > 0 ? merged : null;

            return new CapeDataFetchResult(data, historicalCapesByMarket);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch CAPE data for {Year}-{Month:D2}-{Day:D2}", year, month, day);
            return null;
        }
    }

    private static bool TryExtractHistoricalCapesByMarket(
        JsonElement root,
        out Dictionary<string, List<decimal>> historicalCapesByMarket)
    {
        // Expected shape (optional):
        // {
        //   ...,
        //   "HistoricalCapes": {
        //     "USA": [12.3, 12.5, ...],
        //     "All Country": [ ... ]
        //   }
        // }

        historicalCapesByMarket = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(root, "HistoricalCapes", out var historical) &&
            historical.ValueKind == JsonValueKind.Object)
        {
            foreach (var marketProp in historical.EnumerateObject())
            {
                if (marketProp.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var list = new List<decimal>();
                foreach (var el in marketProp.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                    {
                        list.Add(d);
                    }
                    else if (el.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                    {
                        list.Add(ds);
                    }
                }

                if (list.Count > 0)
                {
                    historicalCapesByMarket[marketProp.Name] = list;
                }
            }
        }

        return historicalCapesByMarket.Count > 0;
    }

    private static bool TryExtractHistoricalCapesByItem(
        JsonElement itemsArray,
        out Dictionary<string, List<decimal>> historicalCapesByMarket)
    {
        // Expected shape (optional per item):
        // [
        //   {
        //     "BoxName": "USA",
        //     ...,
        //     "HistoricalCapes": [12.3, 12.5, ...]
        //   },
        //   ...
        // ]

        historicalCapesByMarket = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

        if (itemsArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in itemsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetPropertyIgnoreCase(item, "BoxName", out var boxNameEl) ||
                boxNameEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var marketKey = boxNameEl.GetString();
            if (string.IsNullOrWhiteSpace(marketKey))
            {
                continue;
            }

            if (!TryGetPropertyIgnoreCase(item, "HistoricalCapes", out var histEl) ||
                histEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var list = new List<decimal>();
            foreach (var el in histEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                {
                    list.Add(d);
                }
                else if (el.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                {
                    list.Add(ds);
                }
            }

            if (list.Count > 0)
            {
                historicalCapesByMarket[marketKey] = list;
            }
        }

        return historicalCapesByMarket.Count > 0;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
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

    // New snapshot payload (backward-compatible with the original array-only format).
    private record StoredCapeSnapshot(
        List<StoredCapeItem> Items,
        Dictionary<string, List<decimal>>? HistoricalCapesByMarket
    );
}
