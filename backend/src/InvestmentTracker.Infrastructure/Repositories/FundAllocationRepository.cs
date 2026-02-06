using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// FundAllocation aggregate repository implementation.
/// </summary>
public class FundAllocationRepository(AppDbContext context) : IFundAllocationRepository
{
    public async Task<IReadOnlyList<FundAllocation>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.FundAllocations
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Purpose)
            .ToListAsync(cancellationToken);
    }

    public async Task<FundAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.FundAllocations
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<FundAllocation> AddAsync(FundAllocation allocation, CancellationToken cancellationToken = default)
    {
        await context.FundAllocations.AddAsync(allocation, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return allocation;
    }

    public async Task UpdateAsync(FundAllocation allocation, CancellationToken cancellationToken = default)
    {
        context.FundAllocations.Update(allocation);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(FundAllocation allocation, CancellationToken cancellationToken = default)
    {
        context.FundAllocations.Remove(allocation);
        await context.SaveChangesAsync(cancellationToken);
    }
}
