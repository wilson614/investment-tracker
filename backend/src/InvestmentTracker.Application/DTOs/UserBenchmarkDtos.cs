using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 使用者基準標的 DTO
/// </summary>
public record UserBenchmarkDto
{
    public Guid Id { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public StockMarket Market { get; init; }
    public string? DisplayName { get; init; }
    public DateTime AddedAt { get; init; }
}

/// <summary>
/// 建立使用者基準標的請求 DTO
/// </summary>
public record CreateUserBenchmarkRequest
{
    public required string Ticker { get; init; }
    public StockMarket Market { get; init; }
    public string? DisplayName { get; init; }
}
