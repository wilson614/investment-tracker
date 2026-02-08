namespace InvestmentTracker.Application.DTOs;

public record AvailableFundsSummaryResponse(
    decimal TotalBankAssets,
    decimal AvailableFunds,
    decimal CommittedFunds,
    CommittedFundsBreakdown Breakdown,
    string Currency
);

public record CommittedFundsBreakdown(
    decimal FixedDepositsPrincipal,
    decimal UnpaidInstallmentBalance,
    IReadOnlyList<FixedDepositSummary> FixedDeposits,
    IReadOnlyList<InstallmentSummary> Installments
);

public record FixedDepositSummary(
    Guid Id,
    string BankName,
    decimal Principal,
    string Currency,
    decimal PrincipalInBaseCurrency
);

public record InstallmentSummary(
    Guid Id,
    string Description,
    string CreditCardName,
    decimal UnpaidBalance
);
