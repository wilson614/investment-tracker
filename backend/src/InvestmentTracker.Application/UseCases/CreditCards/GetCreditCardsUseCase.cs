using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CreditCards;

/// <summary>
/// Get credit cards for current user.
/// </summary>
public class GetCreditCardsUseCase(
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<CreditCardResponse>> ExecuteAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCards = await creditCardRepository.GetAllByUserIdAsync(userId, cancellationToken);

        var filteredCards = includeInactive
            ? creditCards
            : creditCards.Where(cc => cc.IsActive).ToList();

        return filteredCards
            .Select(CreditCardResponse.FromEntity)
            .ToList();
    }
}
