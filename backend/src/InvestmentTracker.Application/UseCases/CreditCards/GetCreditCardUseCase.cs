using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CreditCards;

/// <summary>
/// Get a credit card by ID.
/// </summary>
public class GetCreditCardUseCase(
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CreditCardResponse> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = await creditCardRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", id);

        return CreditCardResponse.FromEntity(creditCard);
    }
}
