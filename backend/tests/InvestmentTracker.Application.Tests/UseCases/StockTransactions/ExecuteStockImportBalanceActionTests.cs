using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.StockTransactions;
using System.ComponentModel.DataAnnotations;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases.StockTransactions;

public class ExecuteStockImportBalanceActionTests
{
    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithNoneDecision_ShouldReturnBalanceActionRequired()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.None,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 10,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");
        result.Summary.InsertedRows.Should().Be(0);
        result.Summary.FailedRows.Should().Be(1);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        rowResult.Message.Should().Be("帳本餘額不足，請選擇處理方式");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        diagnostic.FieldName.Should().Be("balanceAction");
        diagnostic.InvalidValue.Should().Be(nameof(BalanceAction.None));

        rowResult.BalanceDecision.Should().NotBeNull();
        rowResult.BalanceDecision!.RequiredAmount.Should().Be(626425m);
        rowResult.BalanceDecision.AvailableBalance.Should().Be(100000m);
        rowResult.BalanceDecision.Shortfall.Should().Be(526425m);
        rowResult.BalanceDecision.Action.Should().Be(BalanceAction.None);
        rowResult.BalanceDecision.TopUpTransactionType.Should().BeNull();
        rowResult.BalanceDecision.DecisionScope.Should().Be("row_override");
    }

    [Fact]
    public async Task ExecuteAsync_RowNotInSession_ShouldReturnSessionRowMismatchErrorCode()
    {
        // Arrange
        var fixture = new Fixture();
        const int missingRowNumber = 999;
        var request = fixture.BuildExecuteRequest(
            rowAction: null,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: missingRowNumber,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");
        result.Summary.InsertedRows.Should().Be(0);
        result.Summary.FailedRows.Should().Be(1);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.RowNumber.Should().Be(missingRowNumber);
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("SESSION_ROW_MISMATCH");
        rowResult.Message.Should().Be("此列不屬於目前預覽 Session。");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.RowNumber.Should().Be(missingRowNumber);
        diagnostic.ErrorCode.Should().Be("SESSION_ROW_MISMATCH");
        diagnostic.FieldName.Should().Be("rowNumber");
        diagnostic.InvalidValue.Should().Be(missingRowNumber.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_SellWithoutHoldings_ShouldCommitWithPartialPeriodWarning()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 42,
                ticker: "2330",
                unitPrice: 165.5m,
                tradeDate: tradeDate,
                quantity: 100m,
                fees: 6m,
                taxes: 0m,
                tradeSide: "sell")
        };

        var fixture = new Fixture(sessionRows: sessionRows);
        var request = fixture.BuildExecuteRequest(
            rowAction: null,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 42,
            ticker: "2330",
            confirmedTradeSide: "sell");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.RowNumber.Should().Be(42);
        rowResult.Success.Should().BeTrue();
        rowResult.ErrorCode.Should().BeNull();

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.RowNumber.Should().Be(42);
        diagnostic.ErrorCode.Should().Be("PARTIAL_PERIOD_ASSUMPTION");
        diagnostic.FieldName.Should().Be("position");
        diagnostic.InvalidValue.Should().Be("sell");
        diagnostic.Message.Should().Contain("部分期間");

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        addedCurrencyTransactions.Should().ContainSingle(tx => tx.TransactionType == CurrencyTransactionType.OtherIncome);
        addedCurrencyTransactions.Should().NotContain(tx => tx.TransactionType == CurrencyTransactionType.Deposit);
    }

    [Fact]
    public async Task ExecuteAsync_SellWithoutEnoughHoldings_ShouldStillReturnBusinessRuleViolation()
    {
        // Arrange
        var seedStockTransactions = new List<StockTransaction>
        {
            Fixture.CreateStockTransaction(
                portfolioId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 50m,
                pricePerShare: 100m,
                fees: 0m,
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                market: StockMarket.TW,
                currency: Currency.TWD)
        };

        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 52,
                ticker: "2330",
                unitPrice: 165.5m,
                tradeDate: tradeDate,
                quantity: 100m,
                fees: 6m,
                taxes: 0m,
                tradeSide: "sell")
        };

        var fixture = new Fixture(sessionRows: sessionRows, seedStockTransactions: seedStockTransactions);
        var request = fixture.BuildExecuteRequest(
            rowAction: null,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 52,
            ticker: "2330",
            confirmedTradeSide: "sell");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");
        result.Summary.InsertedRows.Should().Be(0);
        result.Summary.FailedRows.Should().Be(1);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.RowNumber.Should().Be(52);
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("BUSINESS_RULE_VIOLATION");
        rowResult.Message.Should().Contain("持股不足");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.RowNumber.Should().Be(52);
        diagnostic.ErrorCode.Should().Be("BUSINESS_RULE_VIOLATION");
        diagnostic.FieldName.Should().Be("position");
        diagnostic.InvalidValue.Should().Be("sell");
    }

    [Fact]
    public async Task ExecuteAsync_SellUsingUniqueExistingMarket_ShouldUseResolvedMarketWithoutPartialWarning()
    {
        // Arrange
        var seedStockTransactions = new List<StockTransaction>
        {
            Fixture.CreateStockTransaction(
                portfolioId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 100m,
                pricePerShare: 100m,
                fees: 0m,
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                market: StockMarket.US,
                currency: Currency.USD)
        };

        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 53,
                ticker: "2330",
                unitPrice: 165.5m,
                tradeDate: tradeDate,
                quantity: 10m,
                fees: 6m,
                taxes: 0m,
                tradeSide: "sell")
        };

        var fixture = new Fixture(sessionRows: sessionRows, seedStockTransactions: seedStockTransactions);
        var request = fixture.BuildExecuteRequest(
            rowAction: null,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 53,
            ticker: "2330",
            confirmedTradeSide: "sell");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);
        result.Errors.Should().BeEmpty("唯一 market 應命中既有持股，不應視為 partial-period");

        var createdStockTransaction = fixture.StockTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IStockTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<StockTransaction>()
            .Single();

        createdStockTransaction.Market.Should().Be(StockMarket.US);
    }

    [Fact]
    public async Task ExecuteAsync_SellWithAmbiguousExistingMarketAndUnsupportedCurrency_ShouldReturnMarketResolutionRequired()
    {
        // Arrange
        var seedStockTransactions = new List<StockTransaction>
        {
            Fixture.CreateStockTransaction(
                portfolioId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 60m,
                pricePerShare: 100m,
                fees: 0m,
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                market: StockMarket.TW,
                currency: Currency.TWD),
            Fixture.CreateStockTransaction(
                portfolioId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                transactionDate: new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 80m,
                pricePerShare: 100m,
                fees: 0m,
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                market: StockMarket.US,
                currency: Currency.USD)
        };

        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 54,
                ticker: "2330",
                unitPrice: 165.5m,
                tradeDate: tradeDate,
                quantity: 100m,
                fees: 6m,
                taxes: 0m,
                tradeSide: "sell",
                currencyCode: "JPY")
        };

        var fixture = new Fixture(sessionRows: sessionRows, seedStockTransactions: seedStockTransactions);
        var request = fixture.BuildExecuteRequest(
            rowAction: null,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 54,
            ticker: "2330",
            confirmedTradeSide: "sell");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");
        result.Summary.InsertedRows.Should().Be(0);
        result.Summary.FailedRows.Should().Be(1);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.RowNumber.Should().Be(54);
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("MARKET_RESOLUTION_REQUIRED");
        rowResult.Message.Should().Contain("無法唯一判斷市場");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.RowNumber.Should().Be(54);
        diagnostic.ErrorCode.Should().Be("MARKET_RESOLUTION_REQUIRED");
        diagnostic.FieldName.Should().Be("market");
        diagnostic.InvalidValue.Should().Be("2330");
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithMarginDecision_ShouldSucceed()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.Margin,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 11,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);
        result.Errors.Should().BeEmpty();

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeTrue();
        rowResult.TransactionId.Should().NotBeNull();
        rowResult.ErrorCode.Should().BeNull();

        fixture.StockTransactionRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithTopUpDecision_ShouldWriteSnapshotForTopUpOnly()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Deposit,
            defaultDecision: null,
            rowNumber: 11,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        var topUpTx = addedCurrencyTransactions.Single(tx => tx.TransactionType == CurrencyTransactionType.Deposit);
        var linkedSpendTx = addedCurrencyTransactions.Single(tx => tx.TransactionType == CurrencyTransactionType.Spend);

        fixture.TxSnapshotServiceMock.Verify(
            x => x.UpsertSnapshotAsync(
                fixture.PortfolioId,
                topUpTx.Id,
                topUpTx.TransactionDate,
                It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.TxSnapshotServiceMock.Verify(
            x => x.UpsertSnapshotAsync(
                fixture.PortfolioId,
                linkedSpendTx.Id,
                linkedSpendTx.TransactionDate,
                It.IsAny<CancellationToken>()),
            Times.Never);

        fixture.TxSnapshotServiceMock.Verify(
            x => x.UpsertSnapshotAsync(
                fixture.PortfolioId,
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithFutureDeposit_ShouldTopUpUsingAsOfBalance()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var seedLedgerTransactions = new List<CurrencyTransaction>
        {
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 100m,
                homeAmount: 100m,
                exchangeRate: 1.0m,
                notes: "seed-past"),
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 1_000_000m,
                homeAmount: 1_000_000m,
                exchangeRate: 1.0m,
                notes: "seed-future")
        };

        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 31,
                ticker: "2330",
                unitPrice: 625m,
                tradeDate: tradeDate)
        };

        var fixture = new Fixture(seedLedgerTransactions: seedLedgerTransactions, sessionRows: sessionRows);
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Deposit,
            defaultDecision: null,
            rowNumber: 31,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        var topUpTx = addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.Deposit).Subject;
        topUpTx.ForeignAmount.Should().Be(626325m, "應以交易日當下可用餘額(100)計算 shortfall");

        var asOfBalance = new CurrencyLedgerService().CalculateBalance(
            seedLedgerTransactions
                .Concat(addedCurrencyTransactions)
                .Where(tx => tx.TransactionDate.Date <= tradeDate.Date));
        asOfBalance.Should().Be(0m, "TopUp + Spend 後交易日餘額不應為負值");
    }

    [Fact]
    public async Task ExecuteAsync_BuyWithSufficientAsOfBalance_WithFutureSpend_ShouldNotCreateTopUp()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var seedLedgerTransactions = new List<CurrencyTransaction>
        {
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 700000m,
                homeAmount: 700000m,
                exchangeRate: 1.0m,
                notes: "seed-past"),
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Spend,
                foreignAmount: 200000m,
                notes: "seed-future")
        };

        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 32,
                ticker: "2330",
                unitPrice: 625m,
                tradeDate: tradeDate)
        };

        var fixture = new Fixture(seedLedgerTransactions: seedLedgerTransactions, sessionRows: sessionRows);
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Deposit,
            defaultDecision: null,
            rowNumber: 32,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.Spend);
        addedCurrencyTransactions.Should().NotContain(
            tx => tx.TransactionType == CurrencyTransactionType.Deposit,
            "交易日當下已有足額餘額時，不應額外補足");
    }

    [Fact]
    public async Task ExecuteAsync_SellWithSufficientHoldings_ShouldNotRequireTopUpAndShouldCreateSellLinkedIncome()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var seedStockTransactions = new List<StockTransaction>
        {
            Fixture.CreateStockTransaction(
                portfolioId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 1000m,
                pricePerShare: 500m,
                fees: 100m,
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                market: StockMarket.TW,
                currency: Currency.TWD)
        };

        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 39,
                ticker: "2330",
                unitPrice: 165.5m,
                tradeDate: tradeDate,
                quantity: 100m,
                fees: 6m,
                taxes: 0m,
                tradeSide: "sell")
        };

        var fixture = new Fixture(sessionRows: sessionRows, seedStockTransactions: seedStockTransactions);
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Deposit,
            defaultDecision: null,
            rowNumber: 39,
            ticker: "2330",
            confirmedTradeSide: "sell");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.OtherIncome,
            "賣出列只應建立連動入帳，不應走買入短缺補足流程");
        addedCurrencyTransactions.Should().NotContain(
            tx => tx.TransactionType == CurrencyTransactionType.Deposit,
            "賣出列不應產生補足交易");
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithFutureSpend_ShouldTopUpExactShortfallWithoutOverTopUp()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var seedLedgerTransactions = new List<CurrencyTransaction>
        {
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 300000m,
                homeAmount: 300000m,
                exchangeRate: 1.0m,
                notes: "seed-past"),
            Fixture.CreateCurrencyTransaction(
                currencyLedgerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                transactionDate: new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
                transactionType: CurrencyTransactionType.Spend,
                foreignAmount: 200000m,
                notes: "seed-future")
        };

        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 33,
                ticker: "2330",
                unitPrice: 625m,
                tradeDate: tradeDate)
        };

        var fixture = new Fixture(seedLedgerTransactions: seedLedgerTransactions, sessionRows: sessionRows);
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Deposit,
            defaultDecision: null,
            rowNumber: 33,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        var topUpTx = addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.Deposit).Subject;
        topUpTx.ForeignAmount.Should().Be(326425m, "補足金額應等於短缺(626425 - 300000)，不可補超過");
    }

    [Fact]
    public async Task ExecuteAsync_PartialPeriodSellThenBuy_ShouldUseSellIncomeWithoutTopUp()
    {
        // Arrange
        var tradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var fixture = new Fixture(
            seedLedgerTransactions: [],
            sessionRows:
            [
                Fixture.BuildSessionRow(
                    rowNumber: 41,
                    ticker: "2330",
                    unitPrice: 1000m,
                    tradeDate: tradeDate,
                    quantity: 100m,
                    fees: 0m,
                    taxes: 0m,
                    tradeSide: "sell"),
                Fixture.BuildSessionRow(
                    rowNumber: 42,
                    ticker: "2330",
                    unitPrice: 500m,
                    tradeDate: tradeDate,
                    quantity: 100m,
                    fees: 0m,
                    taxes: 0m,
                    tradeSide: "buy")
            ]);

        var request = new ExecuteStockImportRequest
        {
            SessionId = fixture.SessionId,
            PortfolioId = fixture.PortfolioId,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 41,
                    Ticker = "2330",
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 42,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                }
            ]
        };

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(2);
        result.Summary.FailedRows.Should().Be(0);

        var warning = result.Errors.Should().ContainSingle().Subject;
        warning.RowNumber.Should().Be(41);
        warning.ErrorCode.Should().Be("PARTIAL_PERIOD_ASSUMPTION");

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        addedCurrencyTransactions.Should().ContainSingle(transaction => transaction.TransactionType == CurrencyTransactionType.OtherIncome);
        addedCurrencyTransactions.Should().ContainSingle(transaction => transaction.TransactionType == CurrencyTransactionType.Spend);
        addedCurrencyTransactions.Should().NotContain(
            transaction => transaction.TransactionType == CurrencyTransactionType.Deposit,
            "先賣後買且可用餘額足夠時，不應再建立補足交易");
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithTopUpWithoutType_OnTwdLedger_ShouldDefaultToDepositAndSucceed()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 12,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(0);

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeTrue();
        rowResult.ErrorCode.Should().BeNull();

        var addedCurrencyTransactions = fixture.CurrencyTransactionRepositoryMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ICurrencyTransactionRepository.AddAsync))
            .Select(invocation => invocation.Arguments[0])
            .OfType<CurrencyTransaction>()
            .ToList();

        addedCurrencyTransactions.Should().ContainSingle(tx => tx.TransactionType == CurrencyTransactionType.Deposit);
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithTopUpWithoutType_OnNonTwdLedger_ShouldReturnBalanceActionRequired()
    {
        // Arrange
        var sessionRows = new List<StockImportSessionRowSnapshotDto>
        {
            Fixture.BuildSessionRow(
                rowNumber: 60,
                ticker: "AAPL",
                unitPrice: 100m,
                currencyCode: "USD")
        };
        var fixture = new Fixture(
            sessionRows: sessionRows,
            boundLedgerCurrencyCode: "USD",
            boundLedgerHomeCurrency: "TWD");
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: null,
            defaultDecision: null,
            rowNumber: 60,
            ticker: "AAPL");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        rowResult.Message.Should().Be("補足餘額需指定交易類型");
        rowResult.BalanceDecision.Should().NotBeNull();
        rowResult.BalanceDecision!.Action.Should().Be(BalanceAction.TopUp);
        rowResult.BalanceDecision.TopUpTransactionType.Should().BeNull();

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.FieldName.Should().Be("topUpTransactionType");
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        diagnostic.InvalidValue.Should().Be(nameof(BalanceAction.TopUp));
    }

    [Fact]
    public void ExecuteAsync_DefaultTopUpWithExchangeBuy_RequestValidationShouldBlock()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.ExchangeBuy
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy"
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().Contain(v =>
            v.ErrorMessage == "DefaultBalanceAction.TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when DefaultBalanceAction.Action is TopUp.");
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithTopUpNonIncomeType_ShouldReturnBalanceActionRequired()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.Withdraw,
            defaultDecision: null,
            rowNumber: 13,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        rowResult.Message.Should().Be("補足餘額的交易類型必須為入帳類型（限 Deposit / InitialBalance / Interest / OtherIncome）");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.FieldName.Should().Be("topUpTransactionType");
        diagnostic.InvalidValue.Should().Be(nameof(CurrencyTransactionType.Withdraw));
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");

        rowResult.BalanceDecision.Should().NotBeNull();
        rowResult.BalanceDecision!.Action.Should().Be(BalanceAction.TopUp);
        rowResult.BalanceDecision.TopUpTransactionType.Should().Be(CurrencyTransactionType.Withdraw);
    }

    [Fact]
    public async Task ExecuteAsync_BuyShortfall_WithTopUpExchangeBuy_ShouldReturnBalanceActionRequired()
    {
        // Arrange
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(
            rowAction: BalanceAction.TopUp,
            rowTopUpType: CurrencyTransactionType.ExchangeBuy,
            defaultDecision: null,
            rowNumber: 12,
            ticker: "2330");

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("rejected");

        var rowResult = result.Results.Should().ContainSingle().Subject;
        rowResult.Success.Should().BeFalse();
        rowResult.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        rowResult.Message.Should().Be("補足餘額的交易類型必須為入帳類型（限 Deposit / InitialBalance / Interest / OtherIncome）");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.FieldName.Should().Be("topUpTransactionType");
        diagnostic.InvalidValue.Should().Be(nameof(CurrencyTransactionType.ExchangeBuy));
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultAndRowOverride_ShouldApplyRowOverridePriority()
    {
        // Arrange
        var fixture = new Fixture();
        var request = new ExecuteStockImportRequest
        {
            SessionId = fixture.SessionId,
            PortfolioId = fixture.PortfolioId,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 21,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 22,
                    Ticker = "2317",
                    ConfirmedTradeSide = "buy",
                    Exclude = false,
                    BalanceAction = BalanceAction.None
                }
            ]
        };

        // Act
        var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.Status.Should().Be("partially_committed");
        result.Summary.InsertedRows.Should().Be(1);
        result.Summary.FailedRows.Should().Be(1);

        var successRow = result.Results.Single(r => r.RowNumber == 21);
        successRow.Success.Should().BeTrue("未覆寫列應使用 default TopUp 並成功");

        var failedRow = result.Results.Single(r => r.RowNumber == 22);
        failedRow.Success.Should().BeFalse("逐列覆寫為 None 應阻擋");
        failedRow.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        failedRow.BalanceDecision.Should().NotBeNull();
        failedRow.BalanceDecision!.Action.Should().Be(BalanceAction.None);
        failedRow.BalanceDecision.TopUpTransactionType.Should().BeNull("非 TopUp 決策不應帶入 default topUp type");
        failedRow.BalanceDecision.DecisionScope.Should().Be("row_override");

        fixture.CurrencyTransactionRepositoryMock.Verify(
            x => x.AddAsync(It.Is<CurrencyTransaction>(tx => tx.TransactionType == CurrencyTransactionType.Deposit), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ExecuteAsync_DefaultTopUpWithoutType_RequestValidationShouldAllow()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy"
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().NotContain(v =>
            v.ErrorMessage == "DefaultBalanceAction.TopUpTransactionType is required when DefaultBalanceAction.Action is TopUp.");
    }

    [Fact]
    public void ExecuteAsync_DefaultNone_RequestValidationShouldAllow()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.None
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy"
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().NotContain(v =>
            v.ErrorMessage == "DefaultBalanceAction.Action must be None, Margin, or TopUp when DefaultBalanceAction is provided.");
        validation.Should().BeEmpty();
    }

    [Fact]
    public void ExecuteAsync_DefaultMarginWithTopUpType_RequestValidationShouldBlock()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.Margin,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy"
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().Contain(v =>
            v.ErrorMessage == "DefaultBalanceAction.TopUpTransactionType is only allowed when DefaultBalanceAction.Action is TopUp.");
    }

    [Fact]
    public void ExecuteAsync_RowTopUpTypeWithoutTopUpAction_RequestValidationShouldBlock()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy",
                    BalanceAction = BalanceAction.Margin,
                    TopUpTransactionType = CurrencyTransactionType.Deposit
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().Contain(v =>
            v.ErrorMessage == "Rows[0].TopUpTransactionType is only allowed when resolved BalanceAction is TopUp.");
    }

    [Fact]
    public void ExecuteAsync_RowTopUpWithNonIncomeType_RequestValidationShouldBlock()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy",
                    BalanceAction = BalanceAction.TopUp,
                    TopUpTransactionType = CurrencyTransactionType.Withdraw
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().Contain(v =>
            v.ErrorMessage == "Rows[0].TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when resolved BalanceAction is TopUp.");
    }

    [Fact]
    public void ExecuteAsync_RowTopUpWithExchangeBuy_RequestValidationShouldBlock()
    {
        // Arrange
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    ConfirmedTradeSide = "buy",
                    BalanceAction = BalanceAction.TopUp,
                    TopUpTransactionType = CurrencyTransactionType.ExchangeBuy
                }
            ]
        };

        // Act
        var validation = Validate(request);

        // Assert
        validation.Should().Contain(v =>
            v.ErrorMessage == "Rows[0].TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when resolved BalanceAction is TopUp.");
    }

    private static IReadOnlyList<ValidationResult> Validate(ExecuteStockImportRequest request)
    {
        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            validationContext,
            validationResults,
            validateAllProperties: true);

        return validationResults;
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();
        public Guid BoundLedgerId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();

        public Mock<IPortfolioRepository> PortfolioRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IStockImportSessionStore> SessionStoreMock { get; } = new();
        public Mock<IStockTransactionRepository> StockTransactionRepositoryMock { get; } = new();
        public Mock<ICurrencyLedgerRepository> CurrencyLedgerRepositoryMock { get; } = new();
        public Mock<ICurrencyTransactionRepository> CurrencyTransactionRepositoryMock { get; } = new();
        public Mock<ITransactionDateExchangeRateService> FxServiceMock { get; } = new();
        public Mock<IMonthlySnapshotService> MonthlySnapshotServiceMock { get; } = new();
        public Mock<ITransactionPortfolioSnapshotService> TxSnapshotServiceMock { get; } = new();
        public Mock<IAppDbTransactionManager> TransactionManagerMock { get; } = new();
        public Mock<IAppDbTransaction> AppDbTransactionMock { get; } = new();

        public ExecuteStockImportUseCase UseCase { get; }

        private readonly Portfolio _portfolio;
        private readonly Domain.Entities.CurrencyLedger _boundLedger;
        private readonly List<StockImportSessionRowSnapshotDto> _sessionRows;

        public Fixture(
            IReadOnlyList<CurrencyTransaction>? seedLedgerTransactions = null,
            IReadOnlyList<StockImportSessionRowSnapshotDto>? sessionRows = null,
            IReadOnlyList<StockTransaction>? seedStockTransactions = null,
            string boundLedgerCurrencyCode = "TWD",
            string boundLedgerHomeCurrency = "TWD")
        {
            _portfolio = new Portfolio(UserId, BoundLedgerId, baseCurrency: "USD", homeCurrency: "TWD", displayName: "US Portfolio");
            typeof(Portfolio).GetProperty(nameof(Portfolio.Id))!.SetValue(_portfolio, PortfolioId);

            _boundLedger = new Domain.Entities.CurrencyLedger(
                UserId,
                currencyCode: boundLedgerCurrencyCode,
                name: $"{boundLedgerCurrencyCode.ToUpperInvariant()} Ledger",
                homeCurrency: boundLedgerHomeCurrency);
            typeof(Domain.Entities.CurrencyLedger).GetProperty(nameof(Domain.Entities.CurrencyLedger.Id))!.SetValue(_boundLedger, BoundLedgerId);

            _sessionRows = sessionRows?.ToList()
                ??
                [
                    BuildSessionRow(rowNumber: 10, ticker: "2330", unitPrice: 625m),
                    BuildSessionRow(rowNumber: 11, ticker: "2330", unitPrice: 625m),
                    BuildSessionRow(rowNumber: 12, ticker: "2330", unitPrice: 625m),
                    BuildSessionRow(rowNumber: 13, ticker: "2330", unitPrice: 625m),
                    BuildSessionRow(rowNumber: 21, ticker: "2330", unitPrice: 625m),
                    BuildSessionRow(rowNumber: 22, ticker: "2317", unitPrice: 100m)
                ];

            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(UserId);
            PortfolioRepositoryMock
                .Setup(x => x.GetByIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_portfolio);

            SessionStoreMock
                .Setup(x => x.GetAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockImportSessionSnapshotDto
                {
                    SessionId = SessionId,
                    UserId = UserId,
                    PortfolioId = PortfolioId,
                    SelectedFormat = "broker_statement",
                    DetectedFormat = "broker_statement",
                    Rows = _sessionRows
                });

            CurrencyLedgerRepositoryMock
                .Setup(x => x.GetByIdAsync(BoundLedgerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_boundLedger);

            var ledgerTransactions = seedLedgerTransactions?.ToList()
                ?? CreateSeedLedgerTransactions(BoundLedgerId).ToList();

            CurrencyTransactionRepositoryMock
                .Setup(x => x.GetByLedgerIdOrderedAsync(BoundLedgerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, CancellationToken _) => ledgerTransactions.OrderBy(tx => tx.TransactionDate).ToList());

            CurrencyTransactionRepositoryMock
                .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) =>
                {
                    ledgerTransactions.Add(tx);
                    return tx;
                });

            var stockTransactions = seedStockTransactions?.ToList() ?? [];

            StockTransactionRepositoryMock
                .Setup(x => x.GetByPortfolioIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, CancellationToken _) => stockTransactions
                    .OrderBy(tx => tx.TransactionDate)
                    .ThenBy(tx => tx.CreatedAt)
                    .ToList());

            StockTransactionRepositoryMock
                .Setup(x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockTransaction tx, CancellationToken _) =>
                {
                    stockTransactions.Add(tx);
                    return tx;
                });

            FxServiceMock
                .Setup(x => x.GetOrFetchAsync("USD", "TWD", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TransactionDateExchangeRateResult
                {
                    Rate = 31.5m,
                    CurrencyPair = "USDTWD",
                    RequestedDate = DateTime.UtcNow.Date,
                    ActualDate = DateTime.UtcNow.Date,
                    Source = "test",
                    FromCache = true
                });

            TransactionManagerMock
                .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AppDbTransactionMock.Object);

            UseCase = new ExecuteStockImportUseCase(
                PortfolioRepositoryMock.Object,
                CurrentUserServiceMock.Object,
                SessionStoreMock.Object,
                StockTransactionRepositoryMock.Object,
                CurrencyLedgerRepositoryMock.Object,
                CurrencyTransactionRepositoryMock.Object,
                FxServiceMock.Object,
                MonthlySnapshotServiceMock.Object,
                TxSnapshotServiceMock.Object,
                new CurrencyLedgerService(),
                new PortfolioCalculator(),
                TransactionManagerMock.Object,
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ExecuteStockImportUseCase>>());
        }

        public ExecuteStockImportRequest BuildExecuteRequest(
            BalanceAction? rowAction,
            CurrencyTransactionType? rowTopUpType,
            StockImportDefaultBalanceDecisionRequest? defaultDecision,
            int rowNumber,
            string ticker,
            string confirmedTradeSide = "buy")
        {
            return new ExecuteStockImportRequest
            {
                SessionId = SessionId,
                PortfolioId = PortfolioId,
                DefaultBalanceAction = defaultDecision,
                Rows =
                [
                    new ExecuteStockImportRowRequest
                    {
                        RowNumber = rowNumber,
                        Ticker = ticker,
                        ConfirmedTradeSide = confirmedTradeSide,
                        Exclude = false,
                        BalanceAction = rowAction,
                        TopUpTransactionType = rowTopUpType
                    }
                ]
            };
        }

        public static StockImportSessionRowSnapshotDto BuildSessionRow(
            int rowNumber,
            string ticker,
            decimal unitPrice,
            DateTime? tradeDate = null,
            decimal quantity = 1000m,
            decimal fees = 1425m,
            decimal taxes = 0m,
            string tradeSide = "buy",
            string currencyCode = "TWD")
        {
            var resolvedTradeDate = tradeDate ?? new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
            var normalizedTradeSide = tradeSide.ToLowerInvariant();
            var netSettlement = normalizedTradeSide == "sell"
                ? (quantity * unitPrice) - (fees + taxes)
                : -(quantity * unitPrice + fees + taxes);

            return new StockImportSessionRowSnapshotDto
            {
                RowNumber = rowNumber,
                TradeDate = resolvedTradeDate,
                Ticker = ticker,
                TradeSide = normalizedTradeSide,
                ConfirmedTradeSide = normalizedTradeSide,
                Quantity = quantity,
                UnitPrice = unitPrice,
                Fees = fees,
                Taxes = taxes,
                NetSettlement = netSettlement,
                Currency = currencyCode,
                Status = "valid",
                ActionsRequired = [],
                IsInvalid = false
            };
        }

        private static IReadOnlyList<CurrencyTransaction> CreateSeedLedgerTransactions(Guid ledgerId)
        {
            return
            [
                CreateCurrencyTransaction(
                    currencyLedgerId: ledgerId,
                    transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                    transactionType: CurrencyTransactionType.Deposit,
                    foreignAmount: 100000m,
                    homeAmount: 100000m,
                    exchangeRate: 1.0m,
                    notes: "seed")
            ];
        }

        public static CurrencyTransaction CreateCurrencyTransaction(
            Guid currencyLedgerId,
            DateTime transactionDate,
            CurrencyTransactionType transactionType,
            decimal foreignAmount,
            decimal? homeAmount = null,
            decimal? exchangeRate = null,
            Guid? relatedStockTransactionId = null,
            string? notes = null)
        {
            var transaction = new CurrencyTransaction(
                currencyLedgerId,
                transactionDate,
                transactionType,
                foreignAmount,
                homeAmount,
                exchangeRate,
                relatedStockTransactionId,
                notes);

            return transaction;
        }

        public static StockTransaction CreateStockTransaction(
            Guid portfolioId,
            DateTime transactionDate,
            string ticker,
            TransactionType transactionType,
            decimal shares,
            decimal pricePerShare,
            decimal fees,
            Guid currencyLedgerId,
            StockMarket market,
            Currency currency,
            decimal exchangeRate = 1.0m)
        {
            var transaction = new StockTransaction(
                portfolioId,
                transactionDate,
                ticker,
                transactionType,
                shares,
                pricePerShare,
                exchangeRate,
                fees,
                currencyLedgerId,
                notes: null,
                market,
                currency);

            return transaction;
        }
    }
}
