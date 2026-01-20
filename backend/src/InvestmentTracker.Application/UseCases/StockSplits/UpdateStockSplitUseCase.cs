using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// 更新拆股（Stock Split）資料的 Use Case。
/// </summary>
public class UpdateStockSplitUseCase(IStockSplitRepository repository)
{
    public async Task<StockSplitDto> ExecuteAsync(
        Guid id,
        UpdateStockSplitRequest request,
        CancellationToken cancellationToken = default)
    {
        var split = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Stock split {id} not found");

        split.Update(request.SplitDate, request.SplitRatio, request.Description);

        await repository.UpdateAsync(split, cancellationToken);

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
