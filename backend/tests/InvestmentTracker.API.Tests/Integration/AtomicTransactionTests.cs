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

        // Create portfolio
        var portfolio = new Portfolio(_testUserId);
        portfolio.SetDescription("Test Portfolio");
        await _portfolioRepository.AddAsync(portfolio);

        // Create currency ledger with initial balance
        var ledger = new CurrencyLedger(_testUserId, "USD", "美金帳戶");
        await _currencyLedgerRepository.AddAsync(ledger);

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
    public async Task BuyStock_WithCurrencyLedger_ShouldDeductFromLedger()
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
            _txDateFxServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 50m, // Total: 500 USD + fees
            ExchangeRate = 31.5m,
            Fees = 5m,
            FundSource = FundSource.CurrencyLedger,
            CurrencyLedgerId = ledger.Id
        };

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Ticker.Should().Be("VWRA");
        result.FundSource.Should().Be(FundSource.CurrencyLedger);

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
    public async Task BuyStock_WithInsufficientBalance_ShouldThrowException()
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
            _txDateFxServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 100,
            PricePerShare = 50m, // Total: 5000 USD - exceeds 1000 USD balance
            ExchangeRate = 31.5m,
            Fees = 5m,
            FundSource = FundSource.CurrencyLedger,
            CurrencyLedgerId = ledger.Id
        };

        // Act & Assert
        var act = async () => await useCase.ExecuteAsync(request);
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Insufficient balance*");

        // Verify no spend transaction was created
        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        transactions.Should().HaveCount(1); // Only initial buy
    }

    [Fact]
    public async Task BuyStock_WithExternalFunding_ShouldNotAffectLedger()
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
            _txDateFxServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 50m,
            ExchangeRate = 31.5m,
            Fees = 5m,
            FundSource = FundSource.None // External funding
        };

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FundSource.Should().Be(FundSource.None);

        // Verify currency ledger balance was NOT deducted
        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        var balance = _currencyLedgerService.CalculateBalance(transactions);
        balance.Should().Be(1000m); // Original balance unchanged
    }

    [Fact]
    public async Task BuyStock_WithWrongUserLedger_ShouldThrowNotFound()
    {
        // Arrange
        var (portfolio, _) = await SetupTestDataAsync();

        // Create ledger for different user (will not be found due to global query filter)
        var otherUserId = Guid.NewGuid();
        var otherLedger = new CurrencyLedger(otherUserId, "USD", "Other User Ledger");
        await _currencyLedgerRepository.AddAsync(otherLedger);
        await _dbContext.SaveChangesAsync();

        var useCase = new CreateStockTransactionUseCase(
            _stockTransactionRepository,
            _portfolioRepository,
            _currencyLedgerRepository,
            _currencyTransactionRepository,
            _portfolioCalculator,
            _currencyLedgerService,
            _currentUserServiceMock.Object,
            _txDateFxServiceMock.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 50m,
            ExchangeRate = 31.5m,
            Fees = 0,
            FundSource = FundSource.CurrencyLedger,
            CurrencyLedgerId = otherLedger.Id
        };

        // Act & Assert
        // Due to global query filter, ledger belongs to other user won't be found
        var act = async () => await useCase.ExecuteAsync(request);
        await act.Should().ThrowAsync<EntityNotFoundException>()
            .WithMessage("*not found*");
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
            _txDateFxServiceMock.Object);

        // First purchase: 200 USD
        var request1 = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow,
            Ticker = "VWRA",
            TransactionType = TransactionType.Buy,
            Shares = 2,
            PricePerShare = 100m,
            ExchangeRate = 31.5m,
            Fees = 0,
            FundSource = FundSource.CurrencyLedger,
            CurrencyLedgerId = ledger.Id
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
            ExchangeRate = 31.5m,
            Fees = 0,
            FundSource = FundSource.CurrencyLedger,
            CurrencyLedgerId = ledger.Id
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
}
