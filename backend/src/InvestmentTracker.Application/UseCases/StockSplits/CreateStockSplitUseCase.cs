using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// 建立拆股（Stock Split）資料的 Use Case。
/// </summary>
public class CreateStockSplitUseCase(IStockSplitRepository repository)
{
    public async Task<StockSplitDto> ExecuteAsync(
        CreateStockSplitRequest request,
        CancellationToken cancellationToken = default)
    {
        // 檢查是否重複建立
        var exists = await repository.ExistsAsync(
            request.Symbol,
            request.Market,
            request.SplitDate,
            cancellationToken);

        if (exists)
            throw new BusinessRuleException(
                $"A stock split already exists for {request.Symbol} on {request.SplitDate:yyyy-MM-dd}");

        var split = new StockSplit(
            request.Symbol,
            request.Market,
            request.SplitDate,
            request.SplitRatio,
            request.Description);

        await repository.AddAsync(split, cancellationToken);

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
