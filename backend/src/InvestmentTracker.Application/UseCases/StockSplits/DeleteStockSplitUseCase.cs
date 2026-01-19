using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// 刪除拆股（Stock Split）資料的 Use Case。
/// </summary>
public class DeleteStockSplitUseCase
{
    private readonly IStockSplitRepository _repository;

    public DeleteStockSplitUseCase(IStockSplitRepository repository)
    {
        _repository = repository;
    }

    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var split = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Stock split {id} not found");

        await _repository.DeleteAsync(id, cancellationToken);
    }
}
