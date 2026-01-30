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
    public void ValidateCurrencyMatchesBoundLedger_UsdPortfolioWithUsdStock_ShouldPass()
    {
        // Arrange - USD portfolio is represented by a USD bound ledger
        var boundLedger = CreateBoundLedger("USD");

        // Act & Assert - should not throw
        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.USD, boundLedger);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_UsdPortfolioWithTwdStock_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var boundLedger = CreateBoundLedger("USD");

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleException>(() =>
            StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.TWD, boundLedger));

        Assert.Contains("TWD", exception.Message);
        Assert.Contains("USD", exception.Message);
    }

    [Fact]
    public void ValidateCurrencyMatchesBoundLedger_TwdPortfolioWithUsdStock_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var boundLedger = CreateBoundLedger("TWD");

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleException>(() =>
            StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(Currency.USD, boundLedger));

        Assert.Contains("USD", exception.Message);
        Assert.Contains("TWD", exception.Message);
    }

    private static global::InvestmentTracker.Domain.Entities.CurrencyLedger CreateBoundLedger(string currencyCode)
    {
        return new global::InvestmentTracker.Domain.Entities.CurrencyLedger(
            userId: Guid.NewGuid(),
            currencyCode: currencyCode,
            name: $"{currencyCode} Ledger",
            homeCurrency: "TWD");
    }
}
