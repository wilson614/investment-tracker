using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Integration tests for atomic stock purchase with currency ledger deduction.
/// Updated for Story 0.2: Portfolio-Ledger 1:1 binding model.
/// </summary>
public class AtomicTransactionTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _stockTransactionRepository;
    private readonly ICurrencyLedgerRepository _currencyLedgerRepository;
    private readonly ICurrencyTransactionRepository _currencyTransactionRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly CurrencyLedgerService _currencyLedgerService;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ITransactionDateExchangeRateService> _txDateFxServiceMock;
    private readonly Mock<IMonthlySnapshotService> _monthlySnapshotServiceMock;
    private readonly Mock<ITransactionPortfolioSnapshotService> _txSnapshotServiceMock;
    private readonly Guid _testUserId = Guid.NewGuid();

    public AtomicTransactionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(_testUserId);

        _txDateFxServiceMock = new Mock<ITransactionDateExchangeRateService>();
        _txDateFxServiceMock
            .Setup(x => x.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionDateExchangeRateResult { Rate = 31.5m, CurrencyPair = "USDTWD" });

        _monthlySnapshotServiceMock = new Mock<IMonthlySnapshotService>();
        _monthlySnapshotServiceMock
            .Setup(x => x.InvalidateFromMonthAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock = new Mock<ITransactionPortfolioSnapshotService>();
        _txSnapshotServiceMock
            .Setup(x => x.UpsertSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _txSnapshotServiceMock
            .Setup(x => x.DeleteSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbContext = new AppDbContext(options, _currentUserServiceMock.Object);
        _portfolioRepository = new PortfolioRepository(_dbContext);
        _stockTransactionRepository = new StockTransactionRepository(_dbContext);
        _currencyLedgerRepository = new CurrencyLedgerRepository(_dbContext);
        _currencyTransactionRepository = new CurrencyTransactionRepository(_dbContext);
        _portfolioCalculator = new PortfolioCalculator();
        _currencyLedgerService = new CurrencyLedgerService();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<(Portfolio portfolio, CurrencyLedger ledger)> SetupTestDataAsync()
    {
        // Create user
        var user = new User("test@example.com", "password", "Test User");
        typeof(User).GetProperty("Id")!.SetValue(user, _testUserId);
        _dbContext.Users.Add(user);

        // Create currency ledger with initial balance
        var ledger = new CurrencyLedger(_testUserId, "USD", "美金帳戶");
        await _currencyLedgerRepository.AddAsync(ledger);

        // Create portfolio bound to the ledger (Story 0.2: 1:1 binding)
        var portfolio = new Portfolio(_testUserId, ledger.Id);
        portfolio.SetDescription("Test Portfolio");
        await _portfolioRepository.AddAsync(portfolio);

        // Add initial currency transaction (exchange buy) to have balance
        var initialTransaction = new CurrencyTransaction(
            ledger.Id,
            DateTime.UtcNow.AddDays(-10),
            CurrencyTransactionType.ExchangeBuy,
            1000m, // 1000 USD
            homeAmount: 31500m, // 31500 TWD
            exchangeRate: 31.5m
        );
        await _currencyTransactionRepository.AddAsync(initialTransaction);

        await _dbContext.SaveChangesAsync();

        return (portfolio, ledger);
    }

    [Fact]
    public async Task BuyStock_WithBoundLedger_ShouldDeductFromLedger()
    {
        // Arrange
        var (portfolio, ledger) = await SetupTestDataAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object,
            _monthlySnapshotServiceMock.Object,
            _txSnapshotServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 50m, // Total: 500 USD + fees
            Fees = 5m,
            Currency = Currency.USD
        };

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Ticker.Should().Be("VWRA");
        result.CurrencyLedgerId.Should().Be(ledger.Id);

        // Verify currency ledger balance was deducted
        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        var balance = _currencyLedgerService.CalculateBalance(transactions);

        // Initial: 1000 USD, Spent: 505 USD (50*10 + 5 fees), Remaining: 495 USD
        balance.Should().Be(495m);

        // Verify spend transaction was created
        transactions.Should().HaveCount(2);
        var spendTx = transactions.First(t => t.TransactionType == CurrencyTransactionType.Spend);
        spendTx.ForeignAmount.Should().Be(505m);
        spendTx.Notes.Should().Contain("VWRA");
    }

    [Fact]
    public async Task BuyStock_WithInsufficientBalance_ShouldAllowNegativeBalance()
    {
        // Arrange
        var (portfolio, ledger) = await SetupTestDataAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object,
            _monthlySnapshotServiceMock.Object,
            _txSnapshotServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 100,
            PricePerShare = 50m, // Total: 5000 USD + fees
            Fees = 5m,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.Margin
        };

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();

        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        var balance = _currencyLedgerService.CalculateBalance(transactions);

        // Initial: 1000 USD, Spent: 5005 USD, Remaining: -4005 USD
        balance.Should().Be(-4005m);

        transactions.Should().HaveCount(2); // 1 initial + 1 spend
        transactions.Count(t => t.TransactionType == CurrencyTransactionType.Spend).Should().Be(1);
        transactions.Count(t => t.TransactionType == CurrencyTransactionType.Deposit).Should().Be(0);
    }

    [Fact]
    public async Task BuyStock_WithCurrencyMismatch_ShouldThrowException()
    {
        // Arrange - Portfolio bound to USD ledger
        var (portfolio, _) = await SetupTestDataAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object,
            _monthlySnapshotServiceMock.Object,
            _txSnapshotServiceMock.Object);

        // Try to buy TWD stock with USD ledger
        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "2330", // Taiwan stock
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 500m,
            Fees = 0,
            Currency = Currency.TWD
        };

        // Act & Assert
        var act = async () => await useCase.ExecuteAsync(request);
        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Message.Should().Contain("不符");
        ex.Which.Message.Should().Contain("TWD");
        ex.Which.Message.Should().Contain("USD");
    }

    [Fact]
    public async Task MultiplePurchases_ShouldDeductCorrectly()
    {
        // Arrange
        var (portfolio, ledger) = await SetupTestDataAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object,
            _monthlySnapshotServiceMock.Object,
            _txSnapshotServiceMock.Object);

        // First purchase: 200 USD
        var request1 = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 2,
            PricePerShare = 100m,
            Fees = 0,
            Currency = Currency.USD
        };

        // Second purchase: 300 USD
        var request2 = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 3,
            PricePerShare = 100m,
            Fees = 0,
            Currency = Currency.USD
        };

        // Act
        await useCase.ExecuteAsync(request1);
        await useCase.ExecuteAsync(request2);

        // Assert
        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        var balance = _currencyLedgerService.CalculateBalance(transactions);

        // Initial: 1000 USD, Spent: 200 + 300 = 500 USD, Remaining: 500 USD
        balance.Should().Be(500m);
        transactions.Should().HaveCount(3); // 1 initial + 2 spends
    }

    [Fact]
    public async Task SellStock_ShouldAddToLedger()
    {
        // Arrange
        var (portfolio, ledger) = await SetupTestDataAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object,
            _monthlySnapshotServiceMock.Object,
            _txSnapshotServiceMock.Object);

        // First buy some shares
        var buyRequest = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 50m,
            Fees = 0,
            Currency = Currency.USD
        };
        await useCase.ExecuteAsync(buyRequest);

        // Now sell some
        var sellRequest = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Sell,
            Shares = 5,
            PricePerShare = 60m, // Sell at higher price
            Fees = 5m,
            Currency = Currency.USD
        };

        // Act
        var result = await useCase.ExecuteAsync(sellRequest);

        // Assert
        result.Should().NotBeNull();
        result.TransactionType.Should().Be(TransactionType.Sell);

        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        var balance = _currencyLedgerService.CalculateBalance(transactions);

        // Initial: 1000, Buy: -500, Sell: +(5*60-5)=295 = 795 USD
        balance.Should().Be(795m);

        // Verify OtherIncome transaction was created for sell
        var incomeTx = transactions.FirstOrDefault(t => t.TransactionType == CurrencyTransactionType.OtherIncome);
        incomeTx.Should().NotBeNull();
        incomeTx!.ForeignAmount.Should().Be(295m); // 5*60 - 5 fees
    }
}
