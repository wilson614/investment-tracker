using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Create a bank account.
/// </summary>
public class CreateBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    private static readonly string[] SupportedCurrencies = ["TWD", "USD", "EUR", "JPY", "CNY", "GBP", "AUD"];
    private static readonly HashSet<string> SupportedCurrencySet = new(SupportedCurrencies, StringComparer.OrdinalIgnoreCase);

    public async Task<BankAccountResponse> ExecuteAsync(
        CreateBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var currency = ValidateCurrency(request.Currency);

        var bankAccount = new Domain.Entities.BankAccount(
            userId,
            request.BankName,
            request.TotalAssets,
            request.InterestRate,
            request.InterestCap,
            request.Note,
            currency,
            request.AccountType);

        if (request.AccountType == BankAccountType.FixedDeposit &&
            request.TermMonths.HasValue &&
            request.StartDate.HasValue)
        {
            bankAccount.ConfigureFixedDeposit(request.TermMonths.Value, request.StartDate.Value);
        }
        else
        {
            bankAccount.SetAccountType(request.AccountType);
        }

        await bankAccountRepository.AddAsync(bankAccount, cancellationToken);

        return BankAccountResponse.FromEntity(bankAccount, interestEstimationService);
    }

    private static string ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new BusinessRuleException("Currency is required.");

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (!SupportedCurrencySet.Contains(normalizedCurrency))
            throw new BusinessRuleException($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");

        return normalizedCurrency;
    }
}
