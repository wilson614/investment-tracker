using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockSplits;

/// <summary>
/// 刪除拆股（Stock Split）資料的 Use Case。
/// </summary>
public class DeleteStockSplitUseCase(IStockSplitRepository repository)
{
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("StockSplit", id);

        await repository.DeleteAsync(id, cancellationToken);
    }
}
