using FluentValidation;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Validators;

public class CreateStockTransactionRequestValidator : AbstractValidator<CreateStockTransactionRequest>
{
    public CreateStockTransactionRequestValidator()
    {
        RuleFor(x => x.PortfolioId)
            .NotEmpty().WithMessage("Portfolio ID is required");

        RuleFor(x => x.Ticker)
            .NotEmpty().WithMessage("Ticker is required")
            .MaximumLength(20).WithMessage("Ticker cannot exceed 20 characters")
            .Matches("^[A-Z0-9.]+$").WithMessage("Ticker must contain only uppercase letters, numbers, and dots");

        RuleFor(x => x.TransactionType)
            .IsInEnum().WithMessage("Invalid transaction type");

        RuleFor(x => x.Shares)
            .GreaterThan(0).WithMessage("Shares must be greater than zero");

        RuleFor(x => x.PricePerShare)
            .GreaterThan(0).WithMessage("Price per share must be greater than zero");

        RuleFor(x => x.ExchangeRate)
            .GreaterThan(0).WithMessage("Exchange rate must be greater than zero");

        RuleFor(x => x.Fees)
            .GreaterThanOrEqualTo(0).WithMessage("Fees cannot be negative");

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("Transaction date is required")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1)).WithMessage("Transaction date cannot be in the future");

        RuleFor(x => x.FundSource)
            .IsInEnum().WithMessage("Invalid fund source");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
