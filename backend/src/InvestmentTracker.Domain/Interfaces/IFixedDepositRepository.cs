using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// FixedDeposit aggregate repository interface.
/// </summary>
public interface IFixedDepositRepository
{
    Task<FixedDeposit?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FixedDeposit>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<FixedDeposit> AddAsync(FixedDeposit entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(FixedDeposit entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(FixedDeposit entity, CancellationToken cancellationToken = default);
}
