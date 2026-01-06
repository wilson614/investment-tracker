using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// DTO for currency ledger.
/// </summary>
public record CurrencyLedgerDto
{
    public Guid Id { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HomeCurrency { get; init; } = "TWD";
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// DTO for currency transaction.
/// </summary>
public record CurrencyTransactionDto
{
    public Guid Id { get; init; }
    public Guid CurrencyLedgerId { get; init; }
    public DateTime TransactionDate { get; init; }
    public CurrencyTransactionType TransactionType { get; init; }
    public decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public Guid? RelatedStockTransactionId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// DTO for currency ledger summary with calculated values.
/// </summary>
public record CurrencyLedgerSummaryDto
{
    public CurrencyLedgerDto Ledger { get; init; } = null!;
    public decimal Balance { get; init; }
    public decimal WeightedAverageCost { get; init; }
    public decimal TotalCostHome { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal? CurrentExchangeRate { get; init; }
    public decimal? CurrentValueHome { get; init; }
    public decimal? UnrealizedPnlHome { get; init; }
    public decimal? UnrealizedPnlPercentage { get; init; }
    public IReadOnlyList<CurrencyTransactionDto> RecentTransactions { get; init; } = Array.Empty<CurrencyTransactionDto>();
}

/// <summary>
/// Request DTO for creating a currency ledger.
/// </summary>
public record CreateCurrencyLedgerRequest
{
    public required string CurrencyCode { get; init; }
    public required string Name { get; init; }
    public string HomeCurrency { get; init; } = "TWD";
}

/// <summary>
/// Request DTO for updating a currency ledger.
/// </summary>
public record UpdateCurrencyLedgerRequest
{
    public required string Name { get; init; }
}

/// <summary>
/// Request DTO for creating a currency transaction.
/// </summary>
public record CreateCurrencyTransactionRequest
{
    public required Guid CurrencyLedgerId { get; init; }
    public required DateTime TransactionDate { get; init; }
    public required CurrencyTransactionType TransactionType { get; init; }
    public required decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public Guid? RelatedStockTransactionId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Request DTO for updating a currency transaction.
/// </summary>
public record UpdateCurrencyTransactionRequest
{
    public required DateTime TransactionDate { get; init; }
    public required CurrencyTransactionType TransactionType { get; init; }
    public required decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public string? Notes { get; init; }
}
