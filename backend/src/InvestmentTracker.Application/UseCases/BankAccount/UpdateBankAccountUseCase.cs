using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Update a bank account.
/// </summary>
public class UpdateBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    private static readonly string[] SupportedCurrencies = ["TWD", "USD", "EUR", "JPY", "CNY", "GBP", "AUD"];
    private static readonly HashSet<string> SupportedCurrencySet = new(SupportedCurrencies, StringComparer.OrdinalIgnoreCase);

    public async Task<BankAccountResponse> ExecuteAsync(
        Guid id,
        UpdateBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", id);

        if (bankAccount.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        bankAccount.SetBankName(request.BankName);
        bankAccount.SetTotalAssets(request.TotalAssets);
        bankAccount.SetInterestSettings(request.InterestRate, request.InterestCap);

        if (request.Currency is not null)
            bankAccount.SetCurrency(ValidateCurrency(request.Currency));

        bankAccount.SetNote(request.Note);

        var targetAccountType = request.AccountType ?? bankAccount.AccountType;
        var changingToFixedDeposit =
            bankAccount.AccountType != BankAccountType.FixedDeposit &&
            targetAccountType == BankAccountType.FixedDeposit;

        if (changingToFixedDeposit &&
            request.TermMonths.HasValue &&
            request.StartDate.HasValue)
        {
            bankAccount.ConfigureFixedDeposit(request.TermMonths.Value, request.StartDate.Value);
        }
        else
        {
            bankAccount.SetAccountType(targetAccountType);

            if (targetAccountType == BankAccountType.FixedDeposit)
            {
                if (request.TermMonths.HasValue)
                    bankAccount.SetTermMonths(request.TermMonths.Value);

                if (request.StartDate.HasValue)
                    bankAccount.SetStartDate(request.StartDate.Value);
            }
        }

        if (targetAccountType == BankAccountType.FixedDeposit)
        {
            if (request.ActualInterest.HasValue)
                bankAccount.SetActualInterest(request.ActualInterest.Value);

            if (request.FixedDepositStatus.HasValue)
                bankAccount.SetFixedDepositStatus(request.FixedDepositStatus.Value);
        }

        await bankAccountRepository.UpdateAsync(bankAccount, cancellationToken);

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
