using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Contains holdings and transactions for a specific user.
/// </summary>
public class Portfolio : BaseEntity
{
    public Guid UserId { get; private set; }
    public string? Description { get; private set; }
    public string BaseCurrency { get; private set; } = "USD";
    public string HomeCurrency { get; private set; } = "TWD";
    public bool IsActive { get; private set; } = true;

    // Navigation properties
    public User User { get; private set; } = null!;

    private readonly List<StockTransaction> _transactions = new();
    public IReadOnlyCollection<StockTransaction> Transactions => _transactions.AsReadOnly();

    // Required by EF Core
    private Portfolio() { }

    public Portfolio(Guid userId, string baseCurrency = "USD", string homeCurrency = "TWD")
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetCurrencies(baseCurrency, homeCurrency);
    }

    public void SetDescription(string? description)
    {
        if (description?.Length > 500)
            throw new ArgumentException("Description cannot exceed 500 characters", nameof(description));

        Description = description?.Trim();
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
