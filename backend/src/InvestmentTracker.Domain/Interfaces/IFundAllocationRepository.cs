using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// FundAllocation aggregate repository interface.
/// </summary>
public interface IFundAllocationRepository
{
    Task<IReadOnlyList<FundAllocation>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<FundAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FundAllocation> AddAsync(FundAllocation allocation, CancellationToken cancellationToken = default);
    Task UpdateAsync(FundAllocation allocation, CancellationToken cancellationToken = default);
    Task DeleteAsync(FundAllocation allocation, CancellationToken cancellationToken = default);
}
