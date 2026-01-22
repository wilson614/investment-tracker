using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// UserPreferences 存取介面
/// </summary>
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}
