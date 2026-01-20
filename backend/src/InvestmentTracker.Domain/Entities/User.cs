using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 使用者實體，包含驗證憑證和個人資訊
/// </summary>
public class User : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    // 導覽屬性
    private readonly List<Portfolio> _portfolios = [];
    public IReadOnlyCollection<Portfolio> Portfolios => _portfolios.AsReadOnly();

    private readonly List<CurrencyLedger> _currencyLedgers = [];
    public IReadOnlyCollection<CurrencyLedger> CurrencyLedgers => _currencyLedgers.AsReadOnly();

    private readonly List<RefreshToken> _refreshTokens = [];
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // EF Core 必要的無參數建構子
    private User() { }

    public User(string email, string passwordHash, string displayName)
    {
        SetEmail(email);
        SetPasswordHash(passwordHash);
        SetDisplayName(displayName);
    }

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        Email = email.Trim().ToLowerInvariant();
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required", nameof(passwordHash));

        PasswordHash = passwordHash;
    }

    public void SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required", nameof(displayName));

        if (displayName.Length > 100)
            throw new ArgumentException("Display name cannot exceed 100 characters", nameof(displayName));

        DisplayName = displayName.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    // 更新方法（Set 方法的別名）
    public void UpdateEmail(string email) => SetEmail(email);
    public void UpdateDisplayName(string displayName) => SetDisplayName(displayName);
    public void UpdatePassword(string passwordHash) => SetPasswordHash(passwordHash);

    public void AddPortfolio(Portfolio portfolio)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        _portfolios.Add(portfolio);
    }

    public void AddCurrencyLedger(CurrencyLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _currencyLedgers.Add(ledger);
    }

    public void AddRefreshToken(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _refreshTokens.Add(token);
    }
}
