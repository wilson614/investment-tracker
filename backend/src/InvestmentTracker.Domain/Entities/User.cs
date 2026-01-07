using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Represents a family member with authentication credentials.
/// </summary>
public class User : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    // Navigation properties
    private readonly List<Portfolio> _portfolios = new();
    public IReadOnlyCollection<Portfolio> Portfolios => _portfolios.AsReadOnly();

    private readonly List<CurrencyLedger> _currencyLedgers = new();
    public IReadOnlyCollection<CurrencyLedger> CurrencyLedgers => _currencyLedgers.AsReadOnly();

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // Required by EF Core
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

    // Update methods (aliases for Set methods)
    public void UpdateEmail(string email) => SetEmail(email);
    public void UpdateDisplayName(string displayName) => SetDisplayName(displayName);
    public void UpdatePassword(string passwordHash) => SetPasswordHash(passwordHash);

    public void AddPortfolio(Portfolio portfolio)
    {
        if (portfolio == null)
            throw new ArgumentNullException(nameof(portfolio));

        _portfolios.Add(portfolio);
    }

    public void AddCurrencyLedger(CurrencyLedger ledger)
    {
        if (ledger == null)
            throw new ArgumentNullException(nameof(ledger));

        _currencyLedgers.Add(ledger);
    }

    public void AddRefreshToken(RefreshToken token)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        _refreshTokens.Add(token);
    }
}
