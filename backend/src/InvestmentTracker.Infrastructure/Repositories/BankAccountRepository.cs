using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// BankAccount entity repository implementation.
/// </summary>
public class BankAccountRepository(AppDbContext context) : IBankAccountRepository
{
    public async Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.BankAccounts
            .FirstOrDefaultAsync(ba => ba.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<BankAccount>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.BankAccounts
            .Where(ba => ba.UserId == userId)
            .OrderByDescending(ba => ba.UpdatedAt)
            .ThenByDescending(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<BankAccount> AddAsync(BankAccount entity, CancellationToken cancellationToken = default)
    {
        await context.BankAccounts.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(BankAccount entity, CancellationToken cancellationToken = default)
    {
        context.BankAccounts.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity != null)
        {
            entity.Deactivate();
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
