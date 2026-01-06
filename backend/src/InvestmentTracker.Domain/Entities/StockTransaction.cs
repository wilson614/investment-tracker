using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Records buy/sell activity for stocks/ETFs.
/// </summary>
public class StockTransaction : BaseEntity
{
    public Guid PortfolioId { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public string Ticker { get; private set; } = string.Empty;
    public TransactionType TransactionType { get; private set; }
    public decimal Shares { get; private set; }
    public decimal PricePerShare { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public decimal Fees { get; private set; }
    public FundSource FundSource { get; private set; } = FundSource.None;
    public Guid? CurrencyLedgerId { get; private set; }
    public string? Notes { get; private set; }
    public bool IsDeleted { get; private set; }

    // Navigation properties
    public Portfolio Portfolio { get; private set; } = null!;
    public CurrencyLedger? CurrencyLedger { get; private set; }

    // Computed properties (not stored in DB)
    /// <summary>Total cost in source currency: (Shares × Price) + Fees</summary>
    public decimal TotalCostSource => (Shares * PricePerShare) + Fees;

    /// <summary>Total cost in home currency: TotalCostSource × ExchangeRate</summary>
    public decimal TotalCostHome => TotalCostSource * ExchangeRate;

    // Required by EF Core
    private StockTransaction() { }

    public StockTransaction(
        Guid portfolioId,
        DateTime transactionDate,
        string ticker,
        TransactionType transactionType,
        decimal shares,
        decimal pricePerShare,
        decimal exchangeRate,
        decimal fees = 0m,
        FundSource fundSource = FundSource.None,
        Guid? currencyLedgerId = null,
        string? notes = null)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required", nameof(portfolioId));

        PortfolioId = portfolioId;
        SetTransactionDate(transactionDate);
        SetTicker(ticker);
        TransactionType = transactionType;
        SetShares(shares);
        SetPricePerShare(pricePerShare);
        SetExchangeRate(exchangeRate);
        SetFees(fees);
        SetFundSource(fundSource, currencyLedgerId);
        SetNotes(notes);
    }

    public void SetTransactionDate(DateTime date)
    {
        if (date > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Transaction date cannot be in the future", nameof(date));

        TransactionDate = date.Date;
    }

    public void SetTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));

        if (ticker.Length > 20)
            throw new ArgumentException("Ticker cannot exceed 20 characters", nameof(ticker));

        Ticker = ticker.Trim().ToUpperInvariant();
    }

    public void SetShares(decimal shares)
    {
        if (shares <= 0)
            throw new ArgumentException("Shares must be positive", nameof(shares));

        // Validate 4 decimal places
        Shares = Math.Round(shares, 4);
    }

    public void SetPricePerShare(decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        PricePerShare = Math.Round(price, 4);
    }

    public void SetExchangeRate(decimal rate)
    {
        if (rate <= 0)
            throw new ArgumentException("Exchange rate must be positive", nameof(rate));

        ExchangeRate = Math.Round(rate, 6);
    }

    public void SetFees(decimal fees)
    {
        if (fees < 0)
            throw new ArgumentException("Fees cannot be negative", nameof(fees));

        Fees = Math.Round(fees, 2);
    }

    public void SetFundSource(FundSource fundSource, Guid? currencyLedgerId = null)
    {
        if (fundSource == FundSource.CurrencyLedger && !currencyLedgerId.HasValue)
            throw new ArgumentException("Currency ledger ID is required when fund source is CurrencyLedger");

        if (fundSource != FundSource.CurrencyLedger && currencyLedgerId.HasValue)
            throw new ArgumentException("Currency ledger ID should only be set when fund source is CurrencyLedger");

        FundSource = fundSource;
        CurrencyLedgerId = currencyLedgerId;
    }

    public void SetNotes(string? notes)
    {
        if (notes?.Length > 500)
            throw new ArgumentException("Notes cannot exceed 500 characters", nameof(notes));

        Notes = notes?.Trim();
    }

    public void MarkAsDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
}
