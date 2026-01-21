using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Benchmark;

/// <summary>
/// 刪除使用者基準標的
/// </summary>
public class DeleteUserBenchmarkUseCase(
    IUserBenchmarkRepository benchmarkRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(Guid benchmarkId, CancellationToken cancellationToken = default)
    {
        var benchmark = await benchmarkRepository.GetByIdAsync(benchmarkId, cancellationToken)
            ?? throw new EntityNotFoundException("UserBenchmark", benchmarkId);

        if (benchmark.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        await benchmarkRepository.DeleteAsync(benchmark, cancellationToken);
    }
}
