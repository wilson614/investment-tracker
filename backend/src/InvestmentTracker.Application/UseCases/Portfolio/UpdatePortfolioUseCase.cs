using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 更新投資組合的 Use Case。
/// </summary>
public class UpdatePortfolioUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService)
{
    public async Task<PortfolioDto> ExecuteAsync(
        Guid portfolioId,
        UpdatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio not found");

        if (portfolio.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        portfolio.SetDescription(request.Description);

        await portfolioRepository.UpdateAsync(portfolio, cancellationToken);

        return portfolio.ToDto();
    }
}
