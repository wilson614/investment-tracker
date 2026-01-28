using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.Tests.UseCases.CurrencyLedger;

/// <summary>
/// Unit tests for TWD (home currency) ledger transaction types.
/// Verifies FR-003: TWD ledger supports Deposit, Withdraw, Interest, Spend, OtherIncome, OtherExpense.
/// </summary>
public class TwdLedgerTransactionTypesTests
{
    private readonly Guid _ledgerId = Guid.NewGuid();
    private readonly DateTime _transactionDate = DateTime.UtcNow.AddDays(-1);

    [Fact]
    public void CurrencyTransaction_Deposit_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.Deposit,
            foreignAmount: 10000m,
            homeAmount: 10000m,
            exchangeRate: 1.0m,
            notes: "TWD Deposit");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.Deposit);
        transaction.ForeignAmount.Should().Be(10000m);
        transaction.HomeAmount.Should().Be(10000m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_Withdraw_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.Withdraw,
            foreignAmount: 5000m,
            homeAmount: 5000m,
            exchangeRate: 1.0m,
            notes: "TWD Withdraw");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.Withdraw);
        transaction.ForeignAmount.Should().Be(5000m);
        transaction.HomeAmount.Should().Be(5000m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_Interest_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.Interest,
            foreignAmount: 100m,
            homeAmount: 100m,
            exchangeRate: 1.0m,
            notes: "TWD Interest");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.Interest);
        transaction.ForeignAmount.Should().Be(100m);
        transaction.HomeAmount.Should().Be(100m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_Spend_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.Spend,
            foreignAmount: 3000m,
            homeAmount: 3000m,
            exchangeRate: 1.0m,
            notes: "Buy TW Stock");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.Spend);
        transaction.ForeignAmount.Should().Be(3000m);
        transaction.HomeAmount.Should().Be(3000m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_OtherIncome_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.OtherIncome,
            foreignAmount: 500m,
            homeAmount: 500m,
            exchangeRate: 1.0m,
            notes: "TWD Other Income");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.OtherIncome);
        transaction.ForeignAmount.Should().Be(500m);
        transaction.HomeAmount.Should().Be(500m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_OtherExpense_ShouldWorkWithTwdLedger()
    {
        // Arrange & Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.OtherExpense,
            foreignAmount: 200m,
            homeAmount: 200m,
            exchangeRate: 1.0m,
            notes: "TWD Other Expense");

        // Assert
        transaction.TransactionType.Should().Be(CurrencyTransactionType.OtherExpense);
        transaction.ForeignAmount.Should().Be(200m);
        transaction.HomeAmount.Should().Be(200m);
        transaction.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public void CurrencyTransaction_TwdLedger_ShouldEnforceExchangeRateOfOne()
    {
        // This test verifies the expected behavior for TWD ledgers:
        // ExchangeRate should be 1.0 and HomeAmount should equal ForeignAmount

        // Arrange
        const decimal foreignAmount = 15000m;
        const decimal expectedHomeAmount = 15000m;
        const decimal expectedExchangeRate = 1.0m;

        // Act
        var transaction = new CurrencyTransaction(
            _ledgerId,
            _transactionDate,
            CurrencyTransactionType.Deposit,
            foreignAmount: foreignAmount,
            homeAmount: expectedHomeAmount,
            exchangeRate: expectedExchangeRate);

        // Assert
        transaction.ExchangeRate.Should().Be(1.0m, "TWD ledger should always have exchange rate of 1.0");
        transaction.HomeAmount.Should().Be(transaction.ForeignAmount, "TWD HomeAmount should equal ForeignAmount");
    }
}
