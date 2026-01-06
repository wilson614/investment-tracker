using FluentValidation;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Validators;

public class CreateCurrencyLedgerRequestValidator : AbstractValidator<CreateCurrencyLedgerRequest>
{
    public CreateCurrencyLedgerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ledger name is required")
            .MaximumLength(100).WithMessage("Ledger name cannot exceed 100 characters");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency code is required")
            .Length(3).WithMessage("Currency code must be exactly 3 characters")
            .Matches("^[A-Z]{3}$").WithMessage("Currency code must be 3 uppercase letters");

        RuleFor(x => x.HomeCurrency)
            .NotEmpty().WithMessage("Home currency is required")
            .Length(3).WithMessage("Currency code must be exactly 3 characters")
            .Matches("^[A-Z]{3}$").WithMessage("Currency code must be 3 uppercase letters");
    }
}
