using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Installment aggregate repository interface.
/// </summary>
public interface IInstallmentRepository
{
    Task<Installment?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Installment>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Installment>> GetByCreditCardIdAsync(Guid creditCardId, CancellationToken cancellationToken = default);
    Task<Installment> AddAsync(Installment entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(Installment entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(Installment entity, CancellationToken cancellationToken = default);
}
