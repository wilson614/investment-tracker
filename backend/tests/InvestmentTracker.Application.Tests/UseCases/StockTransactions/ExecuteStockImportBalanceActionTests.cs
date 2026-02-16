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
    public async Task ExecuteAsync_BuyShortfall_WithTopUpWithoutType_ShouldReturnBalanceActionRequired()
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
    public async Task ExecuteAsync_BuyShortfall_WithTopUpExchangeBuyOnTwdLedger_ShouldReturnBalanceActionRequired()
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
        rowResult.Message.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");
        rowResult.BalanceDecision.Should().NotBeNull();
        rowResult.BalanceDecision!.Action.Should().Be(BalanceAction.TopUp);
        rowResult.BalanceDecision.TopUpTransactionType.Should().Be(CurrencyTransactionType.ExchangeBuy);

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.FieldName.Should().Be("topUpTransactionType");
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");
        diagnostic.InvalidValue.Should().Be(nameof(CurrencyTransactionType.ExchangeBuy));
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
        rowResult.Message.Should().Be("補足餘額的交易類型必須為入帳類型");

        var diagnostic = result.Errors.Should().ContainSingle().Subject;
        diagnostic.FieldName.Should().Be("topUpTransactionType");
        diagnostic.InvalidValue.Should().Be(nameof(CurrencyTransactionType.Withdraw));
        diagnostic.ErrorCode.Should().Be("BALANCE_ACTION_REQUIRED");

        rowResult.BalanceDecision.Should().NotBeNull();
        rowResult.BalanceDecision!.Action.Should().Be(BalanceAction.TopUp);
        rowResult.BalanceDecision.TopUpTransactionType.Should().Be(CurrencyTransactionType.Withdraw);
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
    public void ExecuteAsync_DefaultTopUpWithoutType_RequestValidationShouldBlock()
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
        validation.Should().Contain(v =>
            v.ErrorMessage == "DefaultBalanceAction.TopUpTransactionType is required when DefaultBalanceAction.Action is TopUp.");
    }

    [Fact]
    public void ExecuteAsync_DefaultNone_RequestValidationShouldBlock()
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
        validation.Should().Contain(v =>
            v.ErrorMessage == "DefaultBalanceAction.Action must be Margin or TopUp when DefaultBalanceAction is provided.");
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
            v.ErrorMessage == "Rows[0].TopUpTransactionType must be an income type when resolved BalanceAction is TopUp.");
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

        public Fixture()
        {
            _portfolio = new Portfolio(UserId, BoundLedgerId, baseCurrency: "USD", homeCurrency: "TWD", displayName: "US Portfolio");
            typeof(Portfolio).GetProperty(nameof(Portfolio.Id))!.SetValue(_portfolio, PortfolioId);

            _boundLedger = new Domain.Entities.CurrencyLedger(UserId, currencyCode: "TWD", name: "TWD Ledger", homeCurrency: "TWD");
            typeof(Domain.Entities.CurrencyLedger).GetProperty(nameof(Domain.Entities.CurrencyLedger.Id))!.SetValue(_boundLedger, BoundLedgerId);

            _sessionRows =
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

            CurrencyTransactionRepositoryMock
                .Setup(x => x.GetByLedgerIdOrderedAsync(BoundLedgerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSeedLedgerTransactions(BoundLedgerId));

            CurrencyTransactionRepositoryMock
                .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) => tx);

            StockTransactionRepositoryMock
                .Setup(x => x.GetByPortfolioIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<StockTransaction>());

            StockTransactionRepositoryMock
                .Setup(x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockTransaction tx, CancellationToken _) => tx);

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
                TransactionManagerMock.Object);
        }

        public ExecuteStockImportRequest BuildExecuteRequest(
            BalanceAction? rowAction,
            CurrencyTransactionType? rowTopUpType,
            StockImportDefaultBalanceDecisionRequest? defaultDecision,
            int rowNumber,
            string ticker)
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
                        ConfirmedTradeSide = "buy",
                        Exclude = false,
                        BalanceAction = rowAction,
                        TopUpTransactionType = rowTopUpType
                    }
                ]
            };
        }

        private static StockImportSessionRowSnapshotDto BuildSessionRow(
            int rowNumber,
            string ticker,
            decimal unitPrice)
        {
            return new StockImportSessionRowSnapshotDto
            {
                RowNumber = rowNumber,
                TradeDate = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Ticker = ticker,
                TradeSide = "buy",
                ConfirmedTradeSide = "buy",
                Quantity = 1000m,
                UnitPrice = unitPrice,
                Fees = 1425m,
                Taxes = 0m,
                NetSettlement = -(1000m * unitPrice + 1425m),
                Currency = "TWD",
                Status = "valid",
                ActionsRequired = [],
                IsInvalid = false
            };
        }

        private static IReadOnlyList<CurrencyTransaction> CreateSeedLedgerTransactions(Guid ledgerId)
        {
            return
            [
                new CurrencyTransaction(
                    currencyLedgerId: ledgerId,
                    transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                    transactionType: CurrencyTransactionType.Deposit,
                    foreignAmount: 100000m,
                    homeAmount: 100000m,
                    exchangeRate: 1.0m,
                    notes: "seed")
            ];
        }
    }
}
