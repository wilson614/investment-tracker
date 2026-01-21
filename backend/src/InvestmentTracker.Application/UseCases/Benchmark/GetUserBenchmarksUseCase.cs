using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Benchmark;

/// <summary>
/// 取得使用者基準標的清單
/// </summary>
public class GetUserBenchmarksUseCase(
    IUserBenchmarkRepository benchmarkRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IEnumerable<UserBenchmarkDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User is not authenticated");

        var benchmarks = await benchmarkRepository.GetByUserIdAsync(userId, cancellationToken);

        return benchmarks.Select(b => new UserBenchmarkDto
        {
            Id = b.Id,
            Ticker = b.Ticker,
            Market = b.Market,
            DisplayName = b.DisplayName,
            AddedAt = b.CreatedAt
        });
    }
}
