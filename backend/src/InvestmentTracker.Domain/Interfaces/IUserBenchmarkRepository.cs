using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// UserBenchmark 存取介面
/// </summary>
public interface IUserBenchmarkRepository
{
    Task<IEnumerable<UserBenchmark>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserBenchmark?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserBenchmark?> FindByUserTickerMarketAsync(Guid userId, string ticker, int market, CancellationToken cancellationToken = default);
    Task AddAsync(UserBenchmark benchmark, CancellationToken cancellationToken = default);
    Task DeleteAsync(UserBenchmark benchmark, CancellationToken cancellationToken = default);
}
