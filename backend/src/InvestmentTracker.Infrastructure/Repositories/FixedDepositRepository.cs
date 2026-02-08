using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// FixedDeposit aggregate repository implementation.
/// </summary>
public class FixedDepositRepository(AppDbContext context) : IFixedDepositRepository
{
    public async Task<FixedDeposit?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.FixedDeposits
            .Include(fd => fd.BankAccount)
            .FirstOrDefaultAsync(fd => fd.Id == id && fd.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<FixedDeposit>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.FixedDeposits
            .Where(fd => fd.UserId == userId)
            .Include(fd => fd.BankAccount)
            .OrderByDescending(fd => fd.UpdatedAt)
            .ThenByDescending(fd => fd.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<FixedDeposit> AddAsync(FixedDeposit entity, CancellationToken cancellationToken = default)
    {
        await context.FixedDeposits.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(FixedDeposit entity, CancellationToken cancellationToken = default)
    {
        context.FixedDeposits.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(FixedDeposit entity, CancellationToken cancellationToken = default)
    {
        context.FixedDeposits.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
