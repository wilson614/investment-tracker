using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
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
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

        await portfolioRepository.DeleteAsync(portfolioId, cancellationToken);
    }
}
