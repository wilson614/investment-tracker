using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 外幣帳戶實體，追蹤外幣持有及加權平均成本
/// </summary>
public class CurrencyLedger : BaseEntity
{
    public Guid UserId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string HomeCurrency { get; private set; } = "TWD";
    public bool IsActive { get; private set; } = true;

    // 導覽屬性
    public User User { get; private set; } = null!;

    private readonly List<CurrencyTransaction> _transactions = new();
    public IReadOnlyCollection<CurrencyTransaction> Transactions => _transactions.AsReadOnly();

    // EF Core 必要的無參數建構子
    private CurrencyLedger() { }

    public CurrencyLedger(Guid userId, string currencyCode, string name, string homeCurrency = "TWD")
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetCurrencyCode(currencyCode);
        SetName(name);
        SetHomeCurrency(homeCurrency);
    }

    public void SetCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
            throw new ArgumentException("Currency code must be a 3-letter ISO code", nameof(currencyCode));

        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        if (name.Length > 100)
            throw new ArgumentException("Name cannot exceed 100 characters", nameof(name));

        Name = name.Trim();
    }

    public void SetHomeCurrency(string homeCurrency)
    {
        if (string.IsNullOrWhiteSpace(homeCurrency) || homeCurrency.Length != 3)
            throw new ArgumentException("Home currency must be a 3-letter ISO code", nameof(homeCurrency));

        HomeCurrency = homeCurrency.ToUpperInvariant();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void AddTransaction(CurrencyTransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        _transactions.Add(transaction);
    }
}
