using FluentValidation;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.Validators;

/// <summary>
/// <see cref="UpdateCurrencyTransactionRequest"/> 的輸入驗證器。
/// </summary>
public class UpdateCurrencyTransactionRequestValidator : AbstractValidator<UpdateCurrencyTransactionRequest>
{
    public UpdateCurrencyTransactionRequestValidator()
    {
        RuleFor(x => x.TransactionType)
            .IsInEnum().WithMessage("Invalid transaction type");

        RuleFor(x => x.ForeignAmount)
            .GreaterThan(0).WithMessage("Foreign amount must be greater than zero");

        RuleFor(x => x.HomeAmount)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Home amount is required for exchange transactions")
            .GreaterThan(0).WithMessage("Home amount must be greater than zero")
            .When(x => RequiresHomeAmount(x.TransactionType));

        RuleFor(x => x.HomeAmount)
            .GreaterThan(0).WithMessage("Home amount must be greater than zero")
            .When(x => x.HomeAmount.HasValue && !RequiresHomeAmount(x.TransactionType));

        RuleFor(x => x.ExchangeRate)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Exchange rate is required for exchange transactions")
            .GreaterThan(0).WithMessage("Exchange rate must be greater than zero")
            .When(x => RequiresHomeAmount(x.TransactionType));

        RuleFor(x => x.ExchangeRate)
            .GreaterThan(0).WithMessage("Exchange rate must be greater than zero")
            .When(x => x.ExchangeRate.HasValue && !RequiresHomeAmount(x.TransactionType));

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("Transaction date is required")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1)).WithMessage("Transaction date cannot be in the future");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }

    private static bool RequiresHomeAmount(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell;
}
