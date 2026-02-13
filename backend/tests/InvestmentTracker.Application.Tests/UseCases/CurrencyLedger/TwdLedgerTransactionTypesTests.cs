using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using Moq;

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

    [Theory]
    [InlineData(CurrencyTransactionType.ExchangeBuy, false)]
    [InlineData(CurrencyTransactionType.ExchangeSell, false)]
    [InlineData(CurrencyTransactionType.Deposit, true)]
    [InlineData(CurrencyTransactionType.Withdraw, true)]
    [InlineData(CurrencyTransactionType.Interest, true)]
    [InlineData(CurrencyTransactionType.Spend, true)]
    [InlineData(CurrencyTransactionType.InitialBalance, true)]
    [InlineData(CurrencyTransactionType.OtherIncome, true)]
    [InlineData(CurrencyTransactionType.OtherExpense, true)]
    public void CurrencyTransactionTypePolicy_Validate_TwdLedger_ShouldMatchAllowedMatrix(
        CurrencyTransactionType transactionType,
        bool expectedValid)
    {
        // Act
        var result = CurrencyTransactionTypePolicy.Validate(
            ledgerCurrencyCode: "TWD",
            transactionType,
            amountPresence: new CurrencyTransactionAmountPresence(
                HasAmount: true,
                HasTargetAmount: true));

        // Assert
        result.IsValid.Should().Be(expectedValid);

        if (expectedValid)
        {
            result.Diagnostics.Should().BeEmpty();
            return;
        }

        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ErrorCode.Should().Be(CurrencyTransactionTypePolicy.InvalidTransactionTypeForLedgerErrorCode);
        result.Diagnostics[0].FieldName.Should().Be("transactionType");
        result.Diagnostics[0].InvalidValue.Should().Be(transactionType.ToString());
        result.Diagnostics[0].CorrectionGuidance.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");
    }

    [Theory]
    [InlineData(CurrencyTransactionType.ExchangeBuy)]
    [InlineData(CurrencyTransactionType.ExchangeSell)]
    [InlineData(CurrencyTransactionType.Deposit)]
    [InlineData(CurrencyTransactionType.Withdraw)]
    [InlineData(CurrencyTransactionType.Interest)]
    [InlineData(CurrencyTransactionType.Spend)]
    [InlineData(CurrencyTransactionType.InitialBalance)]
    [InlineData(CurrencyTransactionType.OtherIncome)]
    [InlineData(CurrencyTransactionType.OtherExpense)]
    public void CurrencyTransactionTypePolicy_Validate_NonTwdLedger_ShouldAllowAllCurrentTypes(
        CurrencyTransactionType transactionType)
    {
        // Act
        var result = CurrencyTransactionTypePolicy.Validate(
            ledgerCurrencyCode: "USD",
            transactionType,
            amountPresence: new CurrencyTransactionAmountPresence(
                HasAmount: true,
                HasTargetAmount: true));

        // Assert
        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CurrencyTransactionType.ExchangeBuy)]
    [InlineData(CurrencyTransactionType.ExchangeSell)]
    public void CurrencyTransactionTypePolicy_Validate_WhenExchangeTypeWithoutHomeAmount_ShouldReturnRequiredFieldDiagnostic(
        CurrencyTransactionType transactionType)
    {
        // Act
        var result = CurrencyTransactionTypePolicy.Validate(
            ledgerCurrencyCode: "USD",
            transactionType,
            amountPresence: new CurrencyTransactionAmountPresence(
                HasAmount: true,
                HasTargetAmount: false));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d =>
            d.ErrorCode == CurrencyTransactionTypePolicy.RequiredFieldMissingErrorCode &&
            d.FieldName == "targetAmount" &&
            d.Message == "此交易類型需要 targetAmount");
    }

    [Fact]
    public async Task CreateCurrencyTransactionUseCase_CreateOnTwdLedger_WithExchangeBuy_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var ledger = new global::InvestmentTracker.Domain.Entities.CurrencyLedger(userId, "TWD", "TWD Ledger", "TWD");
        typeof(global::InvestmentTracker.Domain.Entities.CurrencyLedger).GetProperty("Id")!.SetValue(ledger, ledgerId);

        var transactionRepository = new Mock<ICurrencyTransactionRepository>();
        var ledgerRepository = new Mock<ICurrencyLedgerRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var currentUserService = new Mock<ICurrentUserService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();

        ledgerRepository
            .Setup(x => x.GetByIdAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);
        currentUserService.Setup(x => x.UserId).Returns(userId);

        var useCase = new CreateCurrencyTransactionUseCase(
            transactionRepository.Object,
            ledgerRepository.Object,
            portfolioRepository.Object,
            txSnapshotService.Object,
            currentUserService.Object,
            transactionManager.Object);

        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = ledgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.ExchangeBuy,
            ForeignAmount = 100m,
            HomeAmount = 3100m,
            ExchangeRate = 31m
        };

        // Act
        var act = async () => await useCase.ExecuteAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Message.Should().Contain("交易類型不符合此帳本規則");
        ex.Which.Message.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");

        transactionRepository.Verify(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        transactionManager.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateCurrencyTransactionUseCase_CreateOnTwdLedger_WithDeposit_ShouldUseHomeAmountAndExchangeRateOfOne()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var ledger = new global::InvestmentTracker.Domain.Entities.CurrencyLedger(userId, "TWD", "TWD Ledger", "TWD");
        typeof(global::InvestmentTracker.Domain.Entities.CurrencyLedger).GetProperty("Id")!.SetValue(ledger, ledgerId);

        var transactionRepository = new Mock<ICurrencyTransactionRepository>();
        var ledgerRepository = new Mock<ICurrencyLedgerRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var currentUserService = new Mock<ICurrentUserService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();
        var appTransaction = new Mock<IAppDbTransaction>();

        CurrencyTransaction? captured = null;

        ledgerRepository
            .Setup(x => x.GetByIdAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);
        currentUserService.Setup(x => x.UserId).Returns(userId);
        transactionManager
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appTransaction.Object);

        transactionRepository
            .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<CurrencyTransaction, CancellationToken>((tx, _) => captured = tx)
            .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) => tx);

        portfolioRepository
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Portfolio>());

        var useCase = new CreateCurrencyTransactionUseCase(
            transactionRepository.Object,
            ledgerRepository.Object,
            portfolioRepository.Object,
            txSnapshotService.Object,
            currentUserService.Object,
            transactionManager.Object);

        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = ledgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 1234m,
            HomeAmount = 9999m,
            ExchangeRate = 88m,
            Notes = "twd create"
        };

        // Act
        var dto = await useCase.ExecuteAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.HomeAmount.Should().Be(1234m);
        captured.ExchangeRate.Should().Be(1.0m);

        dto.CurrencyLedgerId.Should().Be(ledgerId);
        dto.TransactionType.Should().Be(CurrencyTransactionType.Deposit);
        dto.HomeAmount.Should().Be(1234m);
        dto.ExchangeRate.Should().Be(1.0m);

        appTransaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCurrencyTransactionUseCase_UpdateOnTwdLedger_WithExchangeSell_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var ledger = new global::InvestmentTracker.Domain.Entities.CurrencyLedger(userId, "TWD", "TWD Ledger", "TWD");
        typeof(global::InvestmentTracker.Domain.Entities.CurrencyLedger).GetProperty("Id")!.SetValue(ledger, ledgerId);

        var existing = new CurrencyTransaction(
            ledgerId,
            DateTime.UtcNow.AddDays(-2),
            CurrencyTransactionType.Deposit,
            foreignAmount: 500m,
            homeAmount: 500m,
            exchangeRate: 1.0m,
            notes: "before update");
        typeof(CurrencyTransaction).GetProperty("Id")!.SetValue(existing, transactionId);

        var transactionRepository = new Mock<ICurrencyTransactionRepository>();
        var ledgerRepository = new Mock<ICurrencyLedgerRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var currentUserService = new Mock<ICurrentUserService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();

        transactionRepository
            .Setup(x => x.GetByIdAsync(transactionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        ledgerRepository
            .Setup(x => x.GetByIdAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);
        currentUserService.Setup(x => x.UserId).Returns(userId);

        var useCase = new UpdateCurrencyTransactionUseCase(
            transactionRepository.Object,
            ledgerRepository.Object,
            portfolioRepository.Object,
            txSnapshotService.Object,
            currentUserService.Object,
            transactionManager.Object);

        var request = new UpdateCurrencyTransactionRequest
        {
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.ExchangeSell,
            ForeignAmount = 10m,
            HomeAmount = 300m,
            ExchangeRate = 30m,
            Notes = "invalid for twd"
        };

        // Act
        var act = async () => await useCase.ExecuteAsync(transactionId, request);

        // Assert
        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Message.Should().Contain("交易類型不符合此帳本規則");
        ex.Which.Message.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");

        transactionRepository.Verify(x => x.UpdateAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        transactionManager.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCurrencyTransactionUseCase_UpdateOnTwdLedger_WithDeposit_ShouldKeepExchangeRateOneAndHomeAmountEqualForeign()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var ledger = new global::InvestmentTracker.Domain.Entities.CurrencyLedger(userId, "TWD", "TWD Ledger", "TWD");
        typeof(global::InvestmentTracker.Domain.Entities.CurrencyLedger).GetProperty("Id")!.SetValue(ledger, ledgerId);

        var existing = new CurrencyTransaction(
            ledgerId,
            DateTime.UtcNow.AddDays(-2),
            CurrencyTransactionType.Deposit,
            foreignAmount: 200m,
            homeAmount: 200m,
            exchangeRate: 1.0m,
            notes: "before update");
        typeof(CurrencyTransaction).GetProperty("Id")!.SetValue(existing, transactionId);

        var transactionRepository = new Mock<ICurrencyTransactionRepository>();
        var ledgerRepository = new Mock<ICurrencyLedgerRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var currentUserService = new Mock<ICurrentUserService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();
        var appTransaction = new Mock<IAppDbTransaction>();

        transactionRepository
            .Setup(x => x.GetByIdAsync(transactionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        ledgerRepository
            .Setup(x => x.GetByIdAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);
        currentUserService.Setup(x => x.UserId).Returns(userId);
        transactionManager
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appTransaction.Object);
        portfolioRepository
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Portfolio>());

        var useCase = new UpdateCurrencyTransactionUseCase(
            transactionRepository.Object,
            ledgerRepository.Object,
            portfolioRepository.Object,
            txSnapshotService.Object,
            currentUserService.Object,
            transactionManager.Object);

        var request = new UpdateCurrencyTransactionRequest
        {
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 777m,
            HomeAmount = 1m,
            ExchangeRate = 99m,
            Notes = "update twd"
        };

        // Act
        var dto = await useCase.ExecuteAsync(transactionId, request);

        // Assert
        dto.HomeAmount.Should().Be(777m);
        dto.ExchangeRate.Should().Be(1.0m);

        existing.HomeAmount.Should().Be(777m);
        existing.ExchangeRate.Should().Be(1.0m);
        existing.ForeignAmount.Should().Be(777m);

        transactionRepository.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        appTransaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
