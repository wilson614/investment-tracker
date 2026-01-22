namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 使用者偏好設定的資料傳輸物件
/// </summary>
public record UserPreferencesDto
{
    /// <summary>
    /// YTD benchmark 選擇（JSON 陣列字串）
    /// </summary>
    public string? YtdBenchmarkPreferences { get; init; }

    /// <summary>
    /// CAPE region 選擇（JSON 陣列字串）
    /// </summary>
    public string? CapeRegionPreferences { get; init; }

    /// <summary>
    /// 預設投資組合 ID
    /// </summary>
    public Guid? DefaultPortfolioId { get; init; }
}

/// <summary>
/// 更新使用者偏好設定的請求
/// </summary>
public record UpdateUserPreferencesRequest
{
    /// <summary>
    /// YTD benchmark 選擇（JSON 陣列字串）
    /// </summary>
    public string? YtdBenchmarkPreferences { get; init; }

    /// <summary>
    /// CAPE region 選擇（JSON 陣列字串）
    /// </summary>
    public string? CapeRegionPreferences { get; init; }

    /// <summary>
    /// 預設投資組合 ID
    /// </summary>
    public Guid? DefaultPortfolioId { get; init; }
}
