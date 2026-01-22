using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 建立投資組合的 Use Case。
/// </summary>
public class CreatePortfolioUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService)
{
    public async Task<PortfolioDto> ExecuteAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = new Domain.Entities.Portfolio(
            userId,
            request.BaseCurrency,
            request.HomeCurrency,
            request.DisplayName);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            portfolio.SetDescription(request.Description);
        }

        await portfolioRepository.AddAsync(portfolio, cancellationToken);

        return portfolio.ToDto();
    }
}
