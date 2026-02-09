using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class AvailableFundsServiceTests
{
    private readonly AvailableFundsService _service = new();

    [Fact]
    public void Calculate_EmptyCollections_ReturnsAllZeros()
    {
        var result = _service.Calculate(
            ledgers: [],
            bankAccounts: [],
            installments: [],
            getExchangeRate: _ => 1m);

        result.TotalBankAssets.Should().Be(0m);
        result.FixedDepositsPrincipal.Should().Be(0m);
        result.UnpaidInstallmentBalance.Should().Be(0m);
        result.AvailableFunds.Should().Be(0m);
    }

    [Fact]
    public void Calculate_SingleCurrency_CalculatesUsingTwdAmounts()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var ledgers = new List<LedgerBalance>
        {
            new(Balance: 1_000m, Currency: "TWD")
        };

        var maturedFixedDeposit = CreateMaturedFixedDepositAccount(
            userId,
            bankName: "FD Bank",
            principal: 300m,
            currency: "TWD",
            expectedInterest: 30m);

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "Bank A", totalAssets: 500m, currency: "TWD"),
            maturedFixedDeposit
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Laptop", totalAmount: 600m, numberOfInstallments: 6, remainingInstallments: 3, firstPaymentDate: DateTime.UtcNow.AddMonths(-3))
        };

        var result = _service.Calculate(
            ledgers,
            bankAccounts,
            installments,
            _ => throw new InvalidOperationException("Should not query exchange rate for TWD"));

        result.TotalBankAssets.Should().Be(1_830m); // 1,000(ledger) + 500 + 300 + 30(matured interest)
        result.FixedDepositsPrincipal.Should().Be(330m); // principal + expected interest
        result.UnpaidInstallmentBalance.Should().Be(300m); // 600/6 * 3
        result.AvailableFunds.Should().Be(1_530m);
    }

    [Fact]
    public void Calculate_MultiCurrency_ConvertsUsingExchangeRate()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var ledgers = new List<LedgerBalance>
        {
            new(Balance: 50m, Currency: "USD")
        };

        var maturedFixedDeposit = CreateMaturedFixedDepositAccount(
            userId,
            bankName: "FD US",
            principal: 100m,
            currency: "USD",
            expectedInterest: 5m);

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "TW", totalAssets: 1_000m, currency: "TWD"),
            new(userId, "JP", totalAssets: 10_000m, currency: "JPY"),
            maturedFixedDeposit
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Phone", totalAmount: 120m, numberOfInstallments: 12, remainingInstallments: 6, firstPaymentDate: DateTime.UtcNow.AddMonths(-6))
        };

        var exchangeRateCalls = new List<string>();
        decimal GetExchangeRate(string currency)
        {
            exchangeRateCalls.Add(currency);
            return currency switch
            {
                "USD" => 30m,
                "JPY" => 0.22m,
                _ => throw new InvalidOperationException($"Unexpected currency: {currency}")
            };
        }

        var result = _service.Calculate(ledgers, bankAccounts, installments, GetExchangeRate);

        result.TotalBankAssets.Should().Be(7_850m); // ledger 1,500 + bank 6,200 + matured interest 150
        result.FixedDepositsPrincipal.Should().Be(3_150m); // (100 + 5) * 30
        result.UnpaidInstallmentBalance.Should().Be(60m); // (120 / 12) * 6
        result.AvailableFunds.Should().Be(7_790m);

        exchangeRateCalls.Should().Equal("USD", "JPY", "USD", "USD", "USD");
    }

    [Fact]
    public void Calculate_OnlyMaturedFixedDepositsAreCountedForInterest()
    {
        var userId = Guid.NewGuid();

        var active = CreateFixedDepositAccount(
            userId,
            bankName: "Active FD",
            principal: 200m,
            currency: "TWD",
            status: FixedDepositStatus.Active,
            expectedInterest: 20m,
            maturedByDate: false);

        var matured = CreateFixedDepositAccount(
            userId,
            bankName: "Matured FD",
            principal: 300m,
            currency: "TWD",
            status: FixedDepositStatus.Matured,
            expectedInterest: 30m,
            maturedByDate: true);

        var closed = CreateFixedDepositAccount(
            userId,
            bankName: "Closed FD",
            principal: 400m,
            currency: "TWD",
            status: FixedDepositStatus.Closed,
            expectedInterest: 40m,
            maturedByDate: true,
            actualInterest: 10m);

        var earlyWithdrawal = CreateFixedDepositAccount(
            userId,
            bankName: "Early FD",
            principal: 500m,
            currency: "TWD",
            status: FixedDepositStatus.EarlyWithdrawal,
            expectedInterest: 50m,
            maturedByDate: true,
            actualInterest: 2m);

        var result = _service.Calculate(
            ledgers: [new LedgerBalance(0m, "TWD")],
            bankAccounts:
            [
                new BankAccount(userId, "Savings", totalAssets: 2_000m, currency: "TWD"),
                active,
                matured,
                closed,
                earlyWithdrawal
            ],
            installments: [],
            getExchangeRate: _ => 1m);

        result.FixedDepositsPrincipal.Should().Be(330m); // only matured FD total (principal + interest)
        result.TotalBankAssets.Should().Be(3_430m); // all bank totals 3,400 + matured interest 30
        result.AvailableFunds.Should().Be(3_430m);
    }

    [Fact]
    public void Calculate_OnlyActiveInstallmentsAreCounted()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var active = new Installment(
            creditCardId,
            userId,
            description: "Camera",
            totalAmount: 1_200m,
            numberOfInstallments: 12,
            remainingInstallments: 4,
            firstPaymentDate: DateTime.UtcNow.AddMonths(-8));

        var completed = new Installment(
            creditCardId,
            userId,
            description: "Completed",
            totalAmount: 1_200m,
            numberOfInstallments: 12,
            remainingInstallments: 0,
            firstPaymentDate: DateTime.UtcNow.AddMonths(-12));

        var cancelled = new Installment(
            creditCardId,
            userId,
            description: "Cancelled",
            totalAmount: 1_200m,
            numberOfInstallments: 12,
            remainingInstallments: 8,
            firstPaymentDate: DateTime.UtcNow.AddMonths(-4));
        cancelled.Cancel();

        var result = _service.Calculate(
            ledgers: [],
            bankAccounts: [new BankAccount(userId, "Bank", totalAssets: 5_000m, currency: "TWD")],
            installments: [active, completed, cancelled],
            getExchangeRate: _ => 1m);

        result.UnpaidInstallmentBalance.Should().Be(400m); // 1200/12 * 4
        result.AvailableFunds.Should().Be(4_600m);
    }

    [Fact]
    public void Calculate_ZeroBalances_ReturnsZeroWithoutExtraValues()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var ledgers = new List<LedgerBalance>
        {
            new(Balance: 0m, Currency: "EUR")
        };

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "Bank", totalAssets: 0m, currency: "USD")
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Zero", totalAmount: 1m, numberOfInstallments: 1, remainingInstallments: 0, firstPaymentDate: DateTime.UtcNow.AddMonths(-1))
        };

        var exchangeRateCallCount = 0;

        var result = _service.Calculate(
            ledgers,
            bankAccounts,
            installments,
            _ =>
            {
                exchangeRateCallCount++;
                return 30m;
            });

        result.TotalBankAssets.Should().Be(0m);
        result.FixedDepositsPrincipal.Should().Be(0m);
        result.UnpaidInstallmentBalance.Should().Be(0m);
        result.AvailableFunds.Should().Be(0m);
        exchangeRateCallCount.Should().Be(2); // EUR ledger + USD bank account
    }

    [Fact]
    public void Calculate_NullInputs_ThrowsArgumentNullException()
    {
        var act1 = () => _service.Calculate(
            ledgers: null!,
            bankAccounts: [],
            installments: [],
            getExchangeRate: _ => 1m);

        var act2 = () => _service.Calculate(
            ledgers: [],
            bankAccounts: null!,
            installments: [],
            getExchangeRate: _ => 1m);

        var act3 = () => _service.Calculate(
            ledgers: [],
            bankAccounts: [],
            installments: null!,
            getExchangeRate: _ => 1m);

        var act4 = () => _service.Calculate(
            ledgers: [],
            bankAccounts: [],
            installments: [],
            getExchangeRate: null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
    }

    private static BankAccount CreateMaturedFixedDepositAccount(
        Guid userId,
        string bankName,
        decimal principal,
        string currency,
        decimal expectedInterest)
    {
        var account = new BankAccount(
            userId,
            bankName,
            totalAssets: principal,
            interestRate: 1m,
            currency: currency,
            accountType: BankAccountType.FixedDeposit);

        account.ConfigureFixedDeposit(termMonths: 12, startDate: DateTime.UtcNow.AddMonths(-13));
        account.SetExpectedInterest(expectedInterest);
        account.SetFixedDepositStatus(FixedDepositStatus.Matured);

        return account;
    }

    private static BankAccount CreateFixedDepositAccount(
        Guid userId,
        string bankName,
        decimal principal,
        string currency,
        FixedDepositStatus status,
        decimal expectedInterest,
        bool maturedByDate,
        decimal? actualInterest = null)
    {
        var account = new BankAccount(
            userId,
            bankName,
            totalAssets: principal,
            interestRate: 1m,
            currency: currency,
            accountType: BankAccountType.FixedDeposit);

        account.ConfigureFixedDeposit(
            termMonths: 12,
            startDate: maturedByDate ? DateTime.UtcNow.AddMonths(-13) : DateTime.UtcNow.AddMonths(-1));

        account.SetExpectedInterest(expectedInterest);

        if (actualInterest.HasValue)
            account.SetActualInterest(actualInterest.Value);

        account.SetFixedDepositStatus(status);
        return account;
    }
}
