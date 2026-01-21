using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Benchmark;

/// <summary>
/// 新增使用者基準標的
/// </summary>
public class AddUserBenchmarkUseCase(
    IUserBenchmarkRepository benchmarkRepository,
    ICurrentUserService currentUserService)
{
    public async Task<UserBenchmarkDto> ExecuteAsync(
        CreateUserBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User is not authenticated");

        // 檢查是否已存在相同的 benchmark
        var existing = await benchmarkRepository.FindByUserTickerMarketAsync(
            userId, request.Ticker.ToUpperInvariant(), (int)request.Market, cancellationToken);

        if (existing != null)
            throw new BusinessRuleException($"Benchmark {request.Ticker} in this market already exists");

        var benchmark = new UserBenchmark(
            userId,
            request.Ticker,
            request.Market,
            request.DisplayName);

        await benchmarkRepository.AddAsync(benchmark, cancellationToken);

        return new UserBenchmarkDto
        {
            Id = benchmark.Id,
            Ticker = benchmark.Ticker,
            Market = benchmark.Market,
            DisplayName = benchmark.DisplayName,
            AddedAt = benchmark.CreatedAt
        };
    }
}
