using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// CreditCard aggregate repository implementation.
/// </summary>
public class CreditCardRepository(AppDbContext context) : ICreditCardRepository
{
    public async Task<CreditCard?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.CreditCards
            .Include(cc => cc.Installments)
            .FirstOrDefaultAsync(cc => cc.Id == id && cc.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditCard>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.CreditCards
            .Where(cc => cc.UserId == userId)
            .Include(cc => cc.Installments)
            .OrderByDescending(cc => cc.UpdatedAt)
            .ThenByDescending(cc => cc.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CreditCard> AddAsync(CreditCard entity, CancellationToken cancellationToken = default)
    {
        await context.CreditCards.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(CreditCard entity, CancellationToken cancellationToken = default)
    {
        context.CreditCards.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(CreditCard entity, CancellationToken cancellationToken = default)
    {
        context.CreditCards.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
