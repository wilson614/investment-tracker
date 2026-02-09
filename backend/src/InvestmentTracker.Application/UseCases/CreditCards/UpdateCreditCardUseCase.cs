using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CreditCards;

/// <summary>
/// Update a credit card.
/// </summary>
public class UpdateCreditCardUseCase(
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CreditCardResponse> ExecuteAsync(
        Guid id,
        UpdateCreditCardRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = await creditCardRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", id);

        creditCard.SetBankName(request.BankName);
        creditCard.SetCardName(request.CardName);
        creditCard.SetPaymentDueDay(request.PaymentDueDay);
        creditCard.SetNote(request.Note);

        await creditCardRepository.UpdateAsync(creditCard, cancellationToken);

        return CreditCardResponse.FromEntity(creditCard);
    }
}
