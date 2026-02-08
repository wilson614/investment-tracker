using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// CreditCard aggregate repository interface.
/// </summary>
public interface ICreditCardRepository
{
    Task<CreditCard?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditCard>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CreditCard> AddAsync(CreditCard entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(CreditCard entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(CreditCard entity, CancellationToken cancellationToken = default);
}
