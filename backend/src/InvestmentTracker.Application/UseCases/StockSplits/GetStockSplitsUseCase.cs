using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// 查詢拆股（Stock Split）資料的 Use Case。
/// </summary>
public class GetStockSplitsUseCase(IStockSplitRepository repository)
{
    public async Task<IReadOnlyList<StockSplitDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var splits = await repository.GetAllAsync(cancellationToken);
        return splits.Select(MapToDto).ToList();
    }

    public async Task<StockSplitDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var split = await repository.GetByIdAsync(id, cancellationToken);
        return split != null ? MapToDto(split) : null;
    }

    public async Task<IReadOnlyList<StockSplitDto>> GetBySymbolAsync(
        string symbol,
        StockMarket market,
        CancellationToken cancellationToken = default)
    {
        var splits = await repository.GetBySymbolAsync(symbol, market, cancellationToken);
        return splits.Select(MapToDto).ToList();
    }

    private static StockSplitDto MapToDto(StockSplit split)
    {
        return new StockSplitDto
        {
            Id = split.Id,
            Symbol = split.Symbol,
            Market = split.Market,
            SplitDate = split.SplitDate,
            SplitRatio = split.SplitRatio,
            Description = split.Description,
            CreatedAt = split.CreatedAt,
            UpdatedAt = split.UpdatedAt
        };
    }
}
