using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// UserPreferences Repository 實作
/// </summary>
public class UserPreferencesRepository(AppDbContext context) : IUserPreferencesRepository
{
    public async Task<UserPreferences?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    public async Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        context.UserPreferences.Add(preferences);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        context.UserPreferences.Update(preferences);
        await context.SaveChangesAsync(cancellationToken);
    }
}
