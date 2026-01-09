using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// Use case for updating an existing stock split record.
/// </summary>
public class UpdateStockSplitUseCase
{
    private readonly IStockSplitRepository _repository;

    public UpdateStockSplitUseCase(IStockSplitRepository repository)
    {
        _repository = repository;
    }

    public async Task<StockSplitDto> ExecuteAsync(
        Guid id,
        UpdateStockSplitRequest request,
        CancellationToken cancellationToken = default)
    {
        var split = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Stock split {id} not found");

        split.Update(request.SplitDate, request.SplitRatio, request.Description);

        await _repository.UpdateAsync(split, cancellationToken);

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
