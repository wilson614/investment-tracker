using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Installment aggregate repository implementation.
/// </summary>
public class InstallmentRepository(AppDbContext context) : IInstallmentRepository
{
    public async Task<Installment?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.Installments
            .Include(i => i.CreditCard)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<Installment>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.Installments
            .Where(i => i.UserId == userId)
            .Include(i => i.CreditCard)
            .OrderByDescending(i => i.UpdatedAt)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Installment>> GetByCreditCardIdAsync(Guid creditCardId, CancellationToken cancellationToken = default)
    {
        return await context.Installments
            .Where(i => i.CreditCardId == creditCardId)
            .Include(i => i.CreditCard)
            .OrderByDescending(i => i.UpdatedAt)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Installment> AddAsync(Installment entity, CancellationToken cancellationToken = default)
    {
        await context.Installments.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(Installment entity, CancellationToken cancellationToken = default)
    {
        context.Installments.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Installment entity, CancellationToken cancellationToken = default)
    {
        context.Installments.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
