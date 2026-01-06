using FluentValidation;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Validators;

public class CreateCurrencyTransactionRequestValidator : AbstractValidator<CreateCurrencyTransactionRequest>
{
    public CreateCurrencyTransactionRequestValidator()
    {
        RuleFor(x => x.CurrencyLedgerId)
            .NotEmpty().WithMessage("Currency ledger ID is required");

        RuleFor(x => x.TransactionType)
            .IsInEnum().WithMessage("Invalid transaction type");

        RuleFor(x => x.ForeignAmount)
            .GreaterThan(0).WithMessage("Foreign amount must be greater than zero");

        RuleFor(x => x.ExchangeRate)
            .GreaterThan(0).WithMessage("Exchange rate must be greater than zero")
            .When(x => x.ExchangeRate.HasValue);

        RuleFor(x => x.HomeAmount)
            .GreaterThan(0).WithMessage("Home amount must be greater than zero")
            .When(x => x.HomeAmount.HasValue);

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("Transaction date is required")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1)).WithMessage("Transaction date cannot be in the future");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
