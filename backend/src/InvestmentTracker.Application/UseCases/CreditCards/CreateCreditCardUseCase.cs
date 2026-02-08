using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CreditCards;

/// <summary>
/// Create a credit card.
/// </summary>
public class CreateCreditCardUseCase(
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CreditCardResponse> ExecuteAsync(
        CreateCreditCardRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = new Domain.Entities.CreditCard(
            userId,
            request.BankName,
            request.CardName,
            request.BillingCycleDay,
            request.Note);

        await creditCardRepository.AddAsync(creditCard, cancellationToken);

        return CreditCardResponse.FromEntity(creditCard);
    }
}
