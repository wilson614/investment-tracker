using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 使用者自訂基準標的，用於投資績效比較
/// </summary>
public class UserBenchmark : BaseEntity
{
    public Guid UserId { get; private set; }
    public string Ticker { get; private set; } = string.Empty;
    public StockMarket Market { get; private set; }
    public string? DisplayName { get; private set; }

    // 導覽屬性
    public User User { get; private set; } = null!;

    // EF Core 必要的無參數建構子
    private UserBenchmark() { }

    public UserBenchmark(Guid userId, string ticker, StockMarket market, string? displayName = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetTicker(ticker);
        SetMarket(market);
        SetDisplayName(displayName);
    }

    public void SetTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));

        if (ticker.Length > 20)
            throw new ArgumentException("Ticker cannot exceed 20 characters", nameof(ticker));

        Ticker = ticker.Trim().ToUpperInvariant();
    }

    public void SetMarket(StockMarket market)
    {
        if (!Enum.IsDefined(typeof(StockMarket), market))
            throw new ArgumentException("Invalid market", nameof(market));

        Market = market;
    }

    public void SetDisplayName(string? displayName)
    {
        if (displayName is { Length: > 100 })
            throw new ArgumentException("Display name cannot exceed 100 characters", nameof(displayName));

        DisplayName = displayName?.Trim();
    }

    public void Update(string? displayName)
    {
        SetDisplayName(displayName);
    }
}
