using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 使用者偏好設定實體，儲存使用者的 UI 偏好設定
/// </summary>
public class UserPreferences : BaseEntity
{
    public Guid UserId { get; private set; }

    /// <summary>
    /// YTD benchmark 選擇（JSON 陣列，例如 ["SPY", "VTI"]）
    /// </summary>
    public string? YtdBenchmarkPreferences { get; private set; }

    /// <summary>
    /// CAPE region 選擇（JSON 陣列，例如 ["US", "TW"]）
    /// </summary>
    public string? CapeRegionPreferences { get; private set; }

    /// <summary>
    /// 預設投資組合 ID
    /// </summary>
    public Guid? DefaultPortfolioId { get; private set; }

    // 導覽屬性
    public User User { get; private set; } = null!;
    public Portfolio? DefaultPortfolio { get; private set; }

    // EF Core 必要的無參數建構子
    private UserPreferences() { }

    public UserPreferences(Guid userId)
    {
        UserId = userId;
    }

    public void SetYtdBenchmarkPreferences(string? preferences)
    {
        YtdBenchmarkPreferences = preferences;
    }

    public void SetCapeRegionPreferences(string? preferences)
    {
        CapeRegionPreferences = preferences;
    }

    public void SetDefaultPortfolioId(Guid? portfolioId)
    {
        DefaultPortfolioId = portfolioId;
    }
}
