using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// DTO for portfolio summary.
/// </summary>
public record PortfolioDto
{
    public Guid Id { get; init; }
    public string? Description { get; init; }
    public string BaseCurrency { get; init; } = "USD";
    public string HomeCurrency { get; init; } = "TWD";
    public bool IsActive { get; init; }
    public PortfolioType PortfolioType { get; init; } = PortfolioType.Primary;
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// DTO for stock transaction.
/// </summary>
public record StockTransactionDto
{
    public Guid Id { get; init; }
    public Guid PortfolioId { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public TransactionType TransactionType { get; init; }
    public decimal Shares { get; init; }
    public decimal PricePerShare { get; init; }
    public decimal? ExchangeRate { get; init; }
    public decimal Fees { get; init; }
    public FundSource FundSource { get; init; }
    public Guid? CurrencyLedgerId { get; init; }
    public string? Notes { get; init; }
    public decimal TotalCostSource { get; init; }
    public decimal? TotalCostHome { get; init; }
    /// <summary>Indicates whether this transaction has an exchange rate for home currency conversion.</summary>
    public bool HasExchangeRate { get; init; }
    public decimal? RealizedPnlHome { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Split adjustment fields (FR-052a: display both original and adjusted values)
    /// <summary>Adjusted shares after applying split ratio (= Shares × SplitRatio)</summary>
    public decimal? AdjustedShares { get; init; }
    /// <summary>Adjusted price after applying split ratio (= PricePerShare / SplitRatio)</summary>
    public decimal? AdjustedPricePerShare { get; init; }
    /// <summary>Cumulative split ratio applied to this transaction (1.0 if no split)</summary>
    public decimal SplitRatio { get; init; } = 1.0m;
    /// <summary>Whether this transaction has been adjusted for stock splits</summary>
    public bool HasSplitAdjustment { get; init; }
}

/// <summary>
/// DTO for stock position (calculated from transactions).
/// </summary>
public record StockPositionDto
{
    public string Ticker { get; init; } = string.Empty;
    public decimal TotalShares { get; init; }
    public decimal? TotalCostHome { get; init; }
    public decimal TotalCostSource { get; init; }
    public decimal? AverageCostPerShareHome { get; init; }
    public decimal AverageCostPerShareSource { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? CurrentExchangeRate { get; init; }
    public decimal? CurrentValueHome { get; init; }
    public decimal? UnrealizedPnlHome { get; init; }
    public decimal? UnrealizedPnlPercentage { get; init; }
}

/// <summary>
/// DTO for portfolio summary with positions.
/// </summary>
public record PortfolioSummaryDto
{
    public PortfolioDto Portfolio { get; init; } = null!;
    public IReadOnlyList<StockPositionDto> Positions { get; init; } = Array.Empty<StockPositionDto>();
    public decimal TotalCostHome { get; init; }
    public decimal? TotalValueHome { get; init; }
    public decimal? TotalUnrealizedPnlHome { get; init; }
    public decimal? TotalUnrealizedPnlPercentage { get; init; }
}
