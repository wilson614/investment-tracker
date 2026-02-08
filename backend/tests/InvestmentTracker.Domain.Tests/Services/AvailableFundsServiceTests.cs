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
            bankAccounts: [],
            fixedDeposits: [],
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

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "Bank A", totalAssets: 1_000m, currency: "TWD"),
            new(userId, "Bank B", totalAssets: 500m, currency: "TWD")
        };

        var fixedDeposits = new List<FixedDeposit>
        {
            new(userId, Guid.NewGuid(), principal: 300m, annualInterestRate: 1.5m, termMonths: 12, startDate: DateTime.UtcNow.AddMonths(-2), currency: "TWD")
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Laptop", totalAmount: 600m, numberOfInstallments: 6, remainingInstallments: 3, startDate: DateTime.UtcNow.AddMonths(-3))
        };

        var result = _service.Calculate(
            bankAccounts,
            fixedDeposits,
            installments,
            _ => throw new InvalidOperationException("Should not query exchange rate for TWD"));

        result.TotalBankAssets.Should().Be(1_500m);
        result.FixedDepositsPrincipal.Should().Be(300m);
        result.UnpaidInstallmentBalance.Should().Be(300m); // 600/6 * 3
        result.AvailableFunds.Should().Be(900m);
    }

    [Fact]
    public void Calculate_MultiCurrency_ConvertsUsingExchangeRate()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "TW", totalAssets: 1_000m, currency: "TWD"),
            new(userId, "US", totalAssets: 100m, currency: "USD")
        };

        var fixedDeposits = new List<FixedDeposit>
        {
            new(userId, Guid.NewGuid(), principal: 100m, annualInterestRate: 2m, termMonths: 12, startDate: DateTime.UtcNow.AddMonths(-1), currency: "USD")
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Phone", totalAmount: 120m, numberOfInstallments: 12, remainingInstallments: 6, startDate: DateTime.UtcNow.AddMonths(-6))
        };

        var exchangeRateCalls = new List<string>();
        decimal GetExchangeRate(string currency)
        {
            exchangeRateCalls.Add(currency);
            return currency switch
            {
                "USD" => 30m,
                _ => throw new InvalidOperationException($"Unexpected currency: {currency}")
            };
        }

        var result = _service.Calculate(bankAccounts, fixedDeposits, installments, GetExchangeRate);

        result.TotalBankAssets.Should().Be(4_000m); // 1,000 + (100 * 30)
        result.FixedDepositsPrincipal.Should().Be(3_000m); // 100 * 30
        result.UnpaidInstallmentBalance.Should().Be(60m); // (120 / 12) * 6
        result.AvailableFunds.Should().Be(940m);

        exchangeRateCalls.Should().Equal("USD", "USD");
    }

    [Fact]
    public void Calculate_OnlyActiveFixedDepositsAreCounted()
    {
        var userId = Guid.NewGuid();

        var active = new FixedDeposit(
            userId,
            Guid.NewGuid(),
            principal: 200m,
            annualInterestRate: 1m,
            termMonths: 12,
            startDate: DateTime.UtcNow.AddMonths(-2),
            currency: "TWD");

        var matured = new FixedDeposit(
            userId,
            Guid.NewGuid(),
            principal: 300m,
            annualInterestRate: 1m,
            termMonths: 6,
            startDate: DateTime.UtcNow.AddMonths(-8),
            currency: "TWD");
        matured.MarkAsMatured();

        var closed = new FixedDeposit(
            userId,
            Guid.NewGuid(),
            principal: 400m,
            annualInterestRate: 1m,
            termMonths: 6,
            startDate: DateTime.UtcNow.AddMonths(-8),
            currency: "TWD");
        closed.Close(actualInterest: 10m);

        var earlyWithdrawal = new FixedDeposit(
            userId,
            Guid.NewGuid(),
            principal: 500m,
            annualInterestRate: 1m,
            termMonths: 6,
            startDate: DateTime.UtcNow.AddMonths(-8),
            currency: "TWD");
        earlyWithdrawal.MarkAsEarlyWithdrawal(actualInterest: 2m);

        var result = _service.Calculate(
            bankAccounts: [new BankAccount(userId, "Bank", totalAssets: 2_000m, currency: "TWD")],
            fixedDeposits: [active, matured, closed, earlyWithdrawal],
            installments: [],
            getExchangeRate: _ => 1m);

        result.FixedDepositsPrincipal.Should().Be(200m);
        result.AvailableFunds.Should().Be(1_800m);
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
            startDate: DateTime.UtcNow.AddMonths(-8));

        var completed = new Installment(
            creditCardId,
            userId,
            description: "Completed",
            totalAmount: 1_200m,
            numberOfInstallments: 12,
            remainingInstallments: 0,
            startDate: DateTime.UtcNow.AddMonths(-12));

        var cancelled = new Installment(
            creditCardId,
            userId,
            description: "Cancelled",
            totalAmount: 1_200m,
            numberOfInstallments: 12,
            remainingInstallments: 8,
            startDate: DateTime.UtcNow.AddMonths(-4));
        cancelled.Cancel();

        var result = _service.Calculate(
            bankAccounts: [new BankAccount(userId, "Bank", totalAssets: 5_000m, currency: "TWD")],
            fixedDeposits: [],
            installments: [active, completed, cancelled],
            getExchangeRate: _ => 1m);

        result.UnpaidInstallmentBalance.Should().Be(400m); // 1200/12 * 4
        result.AvailableFunds.Should().Be(4_600m);
    }

    [Fact]
    public void Calculate_ZeroBalances_ReturnsZeroWithoutExchangeRateCalls()
    {
        var userId = Guid.NewGuid();
        var creditCardId = Guid.NewGuid();

        var bankAccounts = new List<BankAccount>
        {
            new(userId, "Bank", totalAssets: 0m, currency: "USD")
        };

        var fixedDeposits = new List<FixedDeposit>
        {
            new(userId, Guid.NewGuid(), principal: 0m, annualInterestRate: 1m, termMonths: 12, startDate: DateTime.UtcNow.AddMonths(-1), currency: "EUR")
        };

        var installments = new List<Installment>
        {
            new(creditCardId, userId, "Zero", totalAmount: 1m, numberOfInstallments: 1, remainingInstallments: 0, startDate: DateTime.UtcNow.AddMonths(-1))
        };

        var exchangeRateCallCount = 0;

        var result = _service.Calculate(
            bankAccounts,
            fixedDeposits,
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
        exchangeRateCallCount.Should().Be(2); // bank USD + deposit EUR conversion paths still execute
    }

    [Fact]
    public void Calculate_NullInputs_ThrowsArgumentNullException()
    {
        var act1 = () => _service.Calculate(
            bankAccounts: null!,
            fixedDeposits: [],
            installments: [],
            getExchangeRate: _ => 1m);

        var act2 = () => _service.Calculate(
            bankAccounts: [],
            fixedDeposits: null!,
            installments: [],
            getExchangeRate: _ => 1m);

        var act3 = () => _service.Calculate(
            bankAccounts: [],
            fixedDeposits: [],
            installments: null!,
            getExchangeRate: _ => 1m);

        var act4 = () => _service.Calculate(
            bankAccounts: [],
            fixedDeposits: [],
            installments: [],
            getExchangeRate: null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
    }
}
