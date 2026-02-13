using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class ReturnCalculatorTests
{
    private readonly IReturnCalculator _calculator = new ReturnCalculator();
    private readonly ReturnCashFlowStrategyProvider _strategyProvider = new(
        new StockTransactionCashFlowStrategy(),
        new CurrencyLedgerCashFlowStrategy());

    [Fact]
    public void CalculateModifiedDietz_WithStartValueAndSingleContribution_ReturnsExpectedRate()
    {
        var startValue = 10000m;
        var endValue = 12000m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlowDate = new DateTime(2024, 7, 1);
        var cashFlows = new List<ReturnCashFlow>
        {
            new(cashFlowDate, 1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (cashFlowDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;
        var expected = (endValue - startValue - 1000m) / (startValue + 1000m * weight);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateModifiedDietz_WithZeroStartValue_UsesWeightedCashFlowsAsDenominator()
    {
        var startValue = 0m;
        var endValue = 1100m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlowDate = new DateTime(2024, 2, 1);
        var cashFlows = new List<ReturnCashFlow>
        {
            new(cashFlowDate, 1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (cashFlowDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;
        var expected = (endValue - startValue - 1000m) / (startValue + 1000m * weight);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateModifiedDietz_WithNonPositiveDenominator_ReturnsNull()
    {
        var startValue = 1000m;
        var endValue = 0m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlows = new List<ReturnCashFlow>
        {
            new(new DateTime(2024, 1, 1), -1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        result.Should().BeNull();
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithSingleCashFlowEvent_LinksSubPeriodsByBeforeAfter()
    {
        var startValue = 1000m;
        var endValue = 1760m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // Before cash flow: 1100 (10% growth), After cash flow: 1600 (+500 contribution)
            new(new DateTime(2024, 7, 1), 1100m, 1600m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // (1100/1000) * (1760/1600) - 1 = 1.1 * 1.1 - 1 = 0.21
        var expected = (1100m / 1000m) * (1760m / 1600m) - 1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithMultipleCashFlowEvents_LinksAllSubPeriods()
    {
        var startValue = 1000m;
        var endValue = 1628m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // r1: 1100/1000 = 1.1, then +500 contribution
            new(new DateTime(2024, 3, 1), 1100m, 1600m),
            // r2: 1680/1600 = 1.05, then -200 withdrawal
            new(new DateTime(2024, 9, 1), 1680m, 1480m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // (1100/1000) * (1680/1600) * (1628/1480) - 1
        var expected = (1100m / 1000m) * (1680m / 1600m) * (1628m / 1480m) - 1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithZeroStartValue_SkipsUntilFirstPositiveStartValue()
    {
        var startValue = 0m;
        var endValue = 1100m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // First investment event: value moves from 0 to 1000 due to contribution
            new(new DateTime(2024, 1, 10), 0m, 1000m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // Only measure from 1000 to 1100
        var expected = 0.1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void GetStrategy_WithActiveBoundLedger_UsesCurrencyLedgerStrategyRegardlessOfExternalEvents()
    {
        // Arrange
        var portfolio = CreatePortfolio();

        var stockTransactions = new List<StockTransaction>
        {
            CreateStockTransaction(portfolio.Id, new DateTime(2025, 2, 1), TransactionType.Buy, 5m, 100m)
        };

        var ledgers = new List<CurrencyLedger>
        {
            CreateLedger(portfolio.UserId, portfolio.BoundCurrencyLedgerId, "USD")
        };

        var currencyTransactions = new List<CurrencyTransaction>
        {
            CreateCurrencyTransaction(
                portfolio.BoundCurrencyLedgerId,
                new DateTime(2025, 2, 1),
                CurrencyTransactionType.Interest,
                10m,
                notes: "internal return")
        };

        // Act
        var strategy = _strategyProvider.GetStrategy(portfolio, stockTransactions, ledgers, currencyTransactions);

        // Assert
        strategy.Should().BeOfType<CurrencyLedgerCashFlowStrategy>();
    }

    [Fact]
    public void CurrencyLedgerStrategy_GetCashFlowEvents_IncludesOnlyExplicitExternalEventsAndExcludesInternalFxEffects()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var boundLedgerId = portfolio.BoundCurrencyLedgerId;
        var relatedStockId = Guid.NewGuid();

        var ledger = CreateLedger(portfolio.UserId, boundLedgerId, "USD");

        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 12, 31);

        var txInitial = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 1, 5), CurrencyTransactionType.InitialBalance, 1000m, createdAt: new DateTime(2025, 1, 5, 1, 0, 0, DateTimeKind.Utc));
        var txDeposit = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 2, 5), CurrencyTransactionType.Deposit, 200m, createdAt: new DateTime(2025, 2, 5, 1, 0, 0, DateTimeKind.Utc));
        var txWithdraw = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 3, 5), CurrencyTransactionType.Withdraw, 50m, createdAt: new DateTime(2025, 3, 5, 1, 0, 0, DateTimeKind.Utc));
        var txOtherIncomeExternal = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 4, 5), CurrencyTransactionType.OtherIncome, 30m, notes: "broker rebate", createdAt: new DateTime(2025, 4, 5, 1, 0, 0, DateTimeKind.Utc));
        var txOtherExpense = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 5, 5), CurrencyTransactionType.OtherExpense, 20m, createdAt: new DateTime(2025, 5, 5, 1, 0, 0, DateTimeKind.Utc));
        var txExchangeBuyExternal = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 6, 5), CurrencyTransactionType.ExchangeBuy, 40m, createdAt: new DateTime(2025, 6, 5, 1, 0, 0, DateTimeKind.Utc));
        var txExchangeSellExternal = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 7, 5), CurrencyTransactionType.ExchangeSell, 15m, createdAt: new DateTime(2025, 7, 5, 1, 0, 0, DateTimeKind.Utc));

        var txSpendInternal = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 8, 5), CurrencyTransactionType.Spend, 80m, relatedStockTransactionId: relatedStockId, notes: "buy stock", createdAt: new DateTime(2025, 8, 5, 1, 0, 0, DateTimeKind.Utc));
        var txOtherIncomeInternal = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 9, 5), CurrencyTransactionType.OtherIncome, 25m, relatedStockTransactionId: relatedStockId, notes: "sell-linked fallback", createdAt: new DateTime(2025, 9, 5, 1, 0, 0, DateTimeKind.Utc));
        var txExchangeBuyInternalFx = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 10, 5), CurrencyTransactionType.ExchangeBuy, 35m, relatedStockTransactionId: relatedStockId, notes: "internal fx effect", createdAt: new DateTime(2025, 10, 5, 1, 0, 0, DateTimeKind.Utc));
        var txExchangeSellInternalFx = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 11, 5), CurrencyTransactionType.ExchangeSell, 12m, relatedStockTransactionId: relatedStockId, notes: "internal fx effect", createdAt: new DateTime(2025, 11, 5, 1, 0, 0, DateTimeKind.Utc));
        var txTopUp = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 12, 5), CurrencyTransactionType.ExchangeBuy, 60m, relatedStockTransactionId: relatedStockId, notes: "補足買入 AAPL 差額", createdAt: new DateTime(2025, 12, 5, 1, 0, 0, DateTimeKind.Utc));

        var strategy = new CurrencyLedgerCashFlowStrategy();

        // Act
        var events = strategy.GetCashFlowEvents(
            portfolio,
            fromDate,
            toDate,
            stockTransactions: [],
            ledgers: [ledger],
            currencyTransactions:
            [
                txInitial,
                txDeposit,
                txWithdraw,
                txOtherIncomeExternal,
                txOtherExpense,
                txExchangeBuyExternal,
                txExchangeSellExternal,
                txSpendInternal,
                txOtherIncomeInternal,
                txExchangeBuyInternalFx,
                txExchangeSellInternalFx,
                txTopUp
            ]);

        // Assert
        events.Select(e => e.TransactionId).Should().Equal(
        [
            txInitial.Id,
            txDeposit.Id,
            txWithdraw.Id,
            txOtherIncomeExternal.Id,
            txOtherExpense.Id,
            txExchangeBuyExternal.Id,
            txExchangeSellExternal.Id,
            txTopUp.Id
        ]);

        events.Should().OnlyContain(e => e.Source == ReturnCashFlowEventSource.CurrencyLedger);

        events.Single(e => e.TransactionId == txWithdraw.Id).Amount.Should().Be(-50m);
        events.Single(e => e.TransactionId == txOtherExpense.Id).Amount.Should().Be(-20m);
        events.Single(e => e.TransactionId == txExchangeSellExternal.Id).Amount.Should().Be(-15m);

        events.Single(e => e.TransactionId == txInitial.Id).Amount.Should().Be(1000m);
        events.Single(e => e.TransactionId == txDeposit.Id).Amount.Should().Be(200m);
        events.Single(e => e.TransactionId == txOtherIncomeExternal.Id).Amount.Should().Be(30m);
        events.Single(e => e.TransactionId == txExchangeBuyExternal.Id).Amount.Should().Be(40m);
        events.Single(e => e.TransactionId == txTopUp.Id).Amount.Should().Be(60m);

        events.Should().OnlyContain(e => e.CurrencyCode == "USD");
    }

    [Fact]
    public void CurrencyLedgerStrategy_GetCashFlowEvents_OnTwdLedger_ExcludesExchangeBuyAndExchangeSell()
    {
        // Arrange
        var portfolio = CreatePortfolio(baseCurrency: "TWD");
        var boundLedgerId = portfolio.BoundCurrencyLedgerId;

        var ledger = CreateLedger(portfolio.UserId, boundLedgerId, "TWD", homeCurrency: "TWD");

        var strategy = new CurrencyLedgerCashFlowStrategy();

        var txExchangeBuy = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 2, 1), CurrencyTransactionType.ExchangeBuy, 100m);
        var txExchangeSell = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 2, 2), CurrencyTransactionType.ExchangeSell, 50m);
        var txDeposit = CreateCurrencyTransaction(boundLedgerId, new DateTime(2025, 2, 3), CurrencyTransactionType.Deposit, 70m);

        // Act
        var events = strategy.GetCashFlowEvents(
            portfolio,
            fromDate: new DateTime(2025, 1, 1),
            toDate: new DateTime(2025, 12, 31),
            stockTransactions: [],
            ledgers: [ledger],
            currencyTransactions: [txExchangeBuy, txExchangeSell, txDeposit]);

        // Assert
        events.Should().ContainSingle();
        events.Single().TransactionId.Should().Be(txDeposit.Id);
        events.Single().Amount.Should().Be(70m);
    }

    private static Portfolio CreatePortfolio(string baseCurrency = "USD", string homeCurrency = "TWD")
    {
        var userId = Guid.NewGuid();
        var boundLedgerId = Guid.NewGuid();

        var portfolio = new Portfolio(
            userId: userId,
            boundCurrencyLedgerId: boundLedgerId,
            baseCurrency: baseCurrency,
            homeCurrency: homeCurrency,
            displayName: "Test Portfolio");

        return portfolio;
    }

    private static CurrencyLedger CreateLedger(Guid userId, Guid ledgerId, string currencyCode, string homeCurrency = "TWD")
    {
        var ledger = new CurrencyLedger(userId, currencyCode, "Test Ledger", homeCurrency);

        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(ledger, [ledgerId]);

        return ledger;
    }

    private static CurrencyTransaction CreateCurrencyTransaction(
        Guid ledgerId,
        DateTime transactionDate,
        CurrencyTransactionType transactionType,
        decimal foreignAmount,
        decimal? homeAmount = null,
        decimal? exchangeRate = null,
        Guid? relatedStockTransactionId = null,
        string? notes = null,
        DateTime? createdAt = null)
    {
        if (transactionType is CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell)
        {
            homeAmount ??= foreignAmount;
            exchangeRate ??= 1m;
        }

        var tx = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: transactionDate,
            transactionType: transactionType,
            foreignAmount: foreignAmount,
            homeAmount: homeAmount,
            exchangeRate: exchangeRate,
            relatedStockTransactionId: relatedStockTransactionId,
            notes: notes);

        tx.CreatedAt = createdAt ?? DateTime.SpecifyKind(transactionDate, DateTimeKind.Utc);
        return tx;
    }

    private static StockTransaction CreateStockTransaction(
        Guid portfolioId,
        DateTime transactionDate,
        TransactionType transactionType,
        decimal shares,
        decimal pricePerShare,
        decimal fees = 0m)
    {
        var tx = new StockTransaction(
            portfolioId: portfolioId,
            transactionDate: transactionDate,
            ticker: "AAPL",
            transactionType: transactionType,
            shares: shares,
            pricePerShare: pricePerShare,
            exchangeRate: 30m,
            fees: fees,
            market: StockMarket.US,
            currency: Currency.USD);

        tx.CreatedAt = DateTime.SpecifyKind(transactionDate, DateTimeKind.Utc);
        return tx;
    }
}
