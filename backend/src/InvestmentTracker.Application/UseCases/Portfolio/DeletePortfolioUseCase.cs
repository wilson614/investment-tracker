using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 刪除（停用）投資組合的 Use Case。
/// </summary>
public class DeletePortfolioUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio not found");

        if (portfolio.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        await portfolioRepository.DeleteAsync(portfolioId, cancellationToken);
    }
}
