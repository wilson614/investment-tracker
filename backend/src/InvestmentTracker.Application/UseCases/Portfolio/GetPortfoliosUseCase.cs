using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
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
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        return portfolios.ToDtos();
    }

    public async Task<PortfolioDto> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

        return portfolio.ToDto();
    }
}
