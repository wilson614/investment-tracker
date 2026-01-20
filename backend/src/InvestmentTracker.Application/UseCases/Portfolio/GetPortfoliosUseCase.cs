using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 取得投資組合清單的 Use Case。
/// </summary>
public class GetPortfoliosUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IEnumerable<PortfolioDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        return portfolios.ToDtos();
    }

    public async Task<PortfolioDto> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio not found");

        if (portfolio.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return portfolio.ToDto();
    }
}
