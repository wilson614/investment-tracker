using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// DTO for stock split data
/// </summary>
public record StockSplitDto
{
    public Guid Id { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public StockMarket Market { get; init; }
    public DateTime SplitDate { get; init; }
    public decimal SplitRatio { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request DTO for creating a stock split
/// </summary>
public record CreateStockSplitRequest
{
    public string Symbol { get; init; } = string.Empty;
    public StockMarket Market { get; init; }
    public DateTime SplitDate { get; init; }
    public decimal SplitRatio { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request DTO for updating a stock split
/// </summary>
public record UpdateStockSplitRequest
{
    public DateTime SplitDate { get; init; }
    public decimal SplitRatio { get; init; }
    public string? Description { get; init; }
}
