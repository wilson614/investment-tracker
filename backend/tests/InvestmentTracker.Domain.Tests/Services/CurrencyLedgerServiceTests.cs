using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class CurrencyLedgerServiceTests
{
    private readonly CurrencyLedgerService _service = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _ledgerId = Guid.NewGuid();

    #region CalculateBalance Tests

    [Fact]
    public void CalculateBalance_WithNoTransactions_ReturnsZero()
    {
        var transactions = Array.Empty<CurrencyTransaction>();

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculateBalance_WithSingleBuy_ReturnsAmount()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(1000m);
    }

    [Fact]
    public void CalculateBalance_WithDeposit_AddsToBalance()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.Deposit, 1000m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(1000m);
    }

    [Fact]
    public void CalculateBalance_WithWithdraw_SubtractsFromBalance()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.Withdraw, 200m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(800m);
    }

    [Fact]
    public void CalculateBalance_WithBuyAndSell_ReturnsNetAmount()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeSell, 300m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(700m);
    }

    [Fact]
    public void CalculateBalance_WithInterest_AddsToBalance()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateInterestTransaction(50m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(1050m);
    }

    [Fact]
    public void CalculateBalance_WithSpend_SubtractsFromBalance()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateSpendTransaction(200m)
        };

        var result = _service.CalculateBalance(transactions);

        result.Should().Be(800m);
    }

    #endregion

    #region CalculateWeightedAverageCost Tests

    [Fact]
    public void CalculateWeightedAverageCost_WithNoTransactions_ReturnsZero()
    {
        var transactions = Array.Empty<CurrencyTransaction>();

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculateWeightedAverageCost_WithSingleBuy_ReturnsExchangeRate()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().Be(32m);
    }

    [Fact]
    public void CalculateWeightedAverageCost_WithTwoBuys_ReturnsWeightedAverage()
    {
        // Buy 1000 USD at 32 TWD = 32000 TWD
        // Buy 500 USD at 31 TWD = 15500 TWD
        // Total: 1500 USD, 47500 TWD
        // Weighted average: 47500 / 1500 = 31.6667
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 500m, 15500m, 31m)
        };

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().BeApproximately(31.6667m, 0.0001m);
    }

    [Fact]
    public void CalculateWeightedAverageCost_WithSellDoesNotAffectAverage()
    {
        // Buy 1000 USD at 32 TWD
        // Sell 500 USD (doesn't affect average cost)
        // Average cost should still be 32
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeSell, 500m, 16000m)
        };

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().Be(32m);
    }

    [Fact]
    public void CalculateWeightedAverageCost_WithInterest_ReducesAverageCost()
    {
        // Buy 1000 USD at 32 TWD = 32000 TWD cost
        // Receive 100 USD interest (0 cost)
        // Total: 1100 USD, 32000 TWD cost
        // Weighted average: 32000 / 1100 = 29.0909
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateInterestTransaction(100m)
        };

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().BeApproximately(29.0909m, 0.0001m);
    }

    [Fact]
    public void CalculateWeightedAverageCost_AfterCompleteSpend_ReturnsZero()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateSpendTransaction(1000m)
        };

        var result = _service.CalculateWeightedAverageCost(transactions);

        result.Should().Be(0m);
    }

    #endregion

    #region CalculateTotalCost Tests

    [Fact]
    public void CalculateTotalCost_WithNoTransactions_ReturnsZero()
    {
        var transactions = Array.Empty<CurrencyTransaction>();

        var result = _service.CalculateTotalCost(transactions);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculateTotalCost_WithBuysAndSells_ReturnsNetCost()
    {
        // Buy 1000 USD for 32000 TWD
        // Sell 500 USD for 16500 TWD (realized gain of 500)
        // Remaining balance: 500 USD
        // Remaining cost basis = 500 * 32 = 16000
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeSell, 500m, 16500m, 33m)
        };

        var result = _service.CalculateTotalCost(transactions);

        result.Should().Be(16000m);
    }

    #endregion

    #region CalculateRealizedPnl Tests

    [Fact]
    public void CalculateRealizedPnl_WithNoSells_ReturnsZero()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.CalculateRealizedPnl(transactions);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculateRealizedPnl_WithProfitableSell_ReturnsGain()
    {
        // Buy 1000 USD at 32 TWD (cost: 32000)
        // Sell 500 USD at 33 TWD (proceeds: 16500)
        // Cost basis for 500 USD: 500 * 32 = 16000
        // Realized PnL: 16500 - 16000 = 500
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeSell, 500m, 16500m, 33m)
        };

        var result = _service.CalculateRealizedPnl(transactions);

        result.Should().Be(500m);
    }

    [Fact]
    public void CalculateRealizedPnl_WithLossSell_ReturnsLoss()
    {
        // Buy 1000 USD at 32 TWD (cost: 32000)
        // Sell 500 USD at 30 TWD (proceeds: 15000)
        // Cost basis for 500 USD: 500 * 32 = 16000
        // Realized PnL: 15000 - 16000 = -1000
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m),
            CreateTransaction(CurrencyTransactionType.ExchangeSell, 500m, 15000m, 30m)
        };

        var result = _service.CalculateRealizedPnl(transactions);

        result.Should().Be(-1000m);
    }

    #endregion

    #region ValidateSpend Tests

    [Fact]
    public void ValidateSpend_WithSufficientBalance_ReturnsTrue()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.ValidateSpend(transactions, 500m);

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSpend_WithExactBalance_ReturnsTrue()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.ValidateSpend(transactions, 1000m);

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSpend_WithInsufficientBalance_ReturnsFalse()
    {
        var transactions = new[]
        {
            CreateTransaction(CurrencyTransactionType.ExchangeBuy, 1000m)
        };

        var result = _service.ValidateSpend(transactions, 1001m);

        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private CurrencyTransaction CreateTransaction(
        CurrencyTransactionType type,
        decimal foreignAmount,
        decimal homeAmount = 32000m,
        decimal exchangeRate = 32m)
    {
        return new CurrencyTransaction(
            _ledgerId,
            DateTime.UtcNow.AddDays(-1),
            type,
            foreignAmount,
            homeAmount,
            exchangeRate);
    }

    private CurrencyTransaction CreateInterestTransaction(decimal foreignAmount)
    {
        return new CurrencyTransaction(
            _ledgerId,
            DateTime.UtcNow.AddDays(-1),
            CurrencyTransactionType.Interest,
            foreignAmount);
    }

    private CurrencyTransaction CreateSpendTransaction(decimal foreignAmount)
    {
        return new CurrencyTransaction(
            _ledgerId,
            DateTime.UtcNow.AddDays(-1),
            CurrencyTransactionType.Spend,
            foreignAmount);
    }

    #endregion
}
