using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 投資組合實體，包含特定使用者的持股和交易記錄
/// </summary>
public class Portfolio : BaseEntity
{
    public Guid UserId { get; private set; }
    public string? Description { get; private set; }
    public string BaseCurrency { get; private set; } = "USD";
    public string HomeCurrency { get; private set; } = "TWD";
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// 投資組合類型，決定幣別處理模式
    /// </summary>
    public PortfolioType PortfolioType { get; private set; } = PortfolioType.Primary;

    /// <summary>
    /// 顯示名稱（例如：「美股投資組合」）
    /// </summary>
    public string? DisplayName { get; private set; }

    // 導覽屬性
    public User User { get; private set; } = null!;

    private readonly List<StockTransaction> _transactions = new();
    public IReadOnlyCollection<StockTransaction> Transactions => _transactions.AsReadOnly();

    // EF Core 必要的無參數建構子
    private Portfolio() { }

    public Portfolio(Guid userId, string baseCurrency = "USD", string homeCurrency = "TWD", PortfolioType portfolioType = PortfolioType.Primary, string? displayName = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetCurrencies(baseCurrency, homeCurrency);
        PortfolioType = portfolioType;
        SetDisplayName(displayName);
    }

    public void SetDescription(string? description)
    {
        if (description?.Length > 500)
            throw new ArgumentException("Description cannot exceed 500 characters", nameof(description));

        Description = description?.Trim();
    }

    public void SetDisplayName(string? displayName)
    {
        if (displayName?.Length > 100)
            throw new ArgumentException("Display name cannot exceed 100 characters", nameof(displayName));

        DisplayName = displayName?.Trim();
    }

    public void SetCurrencies(string baseCurrency, string homeCurrency)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency) || baseCurrency.Length != 3)
            throw new ArgumentException("Base currency must be a 3-letter ISO code", nameof(baseCurrency));

        if (string.IsNullOrWhiteSpace(homeCurrency) || homeCurrency.Length != 3)
            throw new ArgumentException("Home currency must be a 3-letter ISO code", nameof(homeCurrency));

        BaseCurrency = baseCurrency.ToUpperInvariant();
        HomeCurrency = homeCurrency.ToUpperInvariant();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void AddTransaction(StockTransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        _transactions.Add(transaction);
    }
}
