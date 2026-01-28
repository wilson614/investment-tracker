using FluentValidation;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Validators;

/// <summary>
/// <see cref="CreatePortfolioRequest"/> 的輸入驗證器。
/// </summary>
public class CreatePortfolioRequestValidator : AbstractValidator<CreatePortfolioRequest>
{
    public CreatePortfolioRequestValidator()
    {
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency code is required")
            .Length(3).WithMessage("Currency code must be exactly 3 characters")
            .Matches("^[A-Z]{3}$").WithMessage("Currency code must be 3 uppercase letters");

        RuleFor(x => x.HomeCurrency)
            .NotEmpty().WithMessage("Home currency is required")
            .Length(3).WithMessage("Currency code must be exactly 3 characters")
            .Matches("^[A-Z]{3}$").WithMessage("Currency code must be 3 uppercase letters");

        RuleFor(x => x.InitialBalance)
            .GreaterThanOrEqualTo(0).WithMessage("Initial balance must be non-negative")
            .When(x => x.InitialBalance.HasValue);
    }
}
