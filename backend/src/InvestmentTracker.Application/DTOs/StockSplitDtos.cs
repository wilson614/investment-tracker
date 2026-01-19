using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 拆股資料的 DTO。
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
/// 建立拆股資料的請求 DTO。
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
/// 更新拆股資料的請求 DTO。
/// </summary>
public record UpdateStockSplitRequest
{
    public DateTime SplitDate { get; init; }
    public decimal SplitRatio { get; init; }
    public string? Description { get; init; }
}
