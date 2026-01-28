using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
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
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

        portfolio.SetDescription(request.Description);

        if (request.BoundCurrencyLedgerId.HasValue &&
            request.BoundCurrencyLedgerId.Value != portfolio.BoundCurrencyLedgerId)
        {
            throw new BusinessRuleException("Bound currency ledger cannot be changed");
        }

        await portfolioRepository.UpdateAsync(portfolio, cancellationToken);

        return portfolio.ToDto();
    }
}
