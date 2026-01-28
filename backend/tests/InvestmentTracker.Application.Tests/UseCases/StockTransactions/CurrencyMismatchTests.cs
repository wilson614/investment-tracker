using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;

namespace InvestmentTracker.Application.Tests.UseCases.StockTransactions;

/// <summary>
/// Tests for FR-005: Currency mismatch validation between stock and bound ledger.
/// </summary>
public class CurrencyMismatchTests
{
    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_UsdStockWithUsdLedger_ShouldPass()
    {
        // Arrange
        var ledger = CreateLedger("USD");

        // Act & Assert - should not throw
        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.USD, ledger);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_TwdStockWithTwdLedger_ShouldPass()
    {
        // Arrange
        var ledger = CreateLedger("TWD");

        // Act & Assert - should not throw
        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.TWD, ledger);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_TwdStockWithUsdLedger_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var ledger = CreateLedger("USD");

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleException>(() =>
            StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.TWD, ledger));

        Assert.Contains("TWD", exception.Message);
        Assert.Contains("USD", exception.Message);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_UsdStockWithTwdLedger_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var ledger = CreateLedger("TWD");

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleException>(() =>
            StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.USD, ledger));

        Assert.Contains("USD", exception.Message);
        Assert.Contains("TWD", exception.Message);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_GbpStockWithUsdLedger_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var ledger = CreateLedger("USD");

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleException>(() =>
            StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.GBP, ledger));

        Assert.Contains("GBP", exception.Message);
        Assert.Contains("USD", exception.Message);
    }

    private static Domain.Entities.CurrencyLedger CreateLedger(string currencyCode)
    {
        return new Domain.Entities.CurrencyLedger(
            userId: Guid.NewGuid(),
            currencyCode: currencyCode,
            name: $"{currencyCode} Ledger",
            homeCurrency: "TWD");
    }
}
