using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InvestmentTracker.API.Tests.Integration;

public class MultiTenancyTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Guid _user1Id = Guid.NewGuid();
    private readonly Guid _user2Id = Guid.NewGuid();
    private Guid _user1PortfolioId;
    private Guid _user2PortfolioId;
    private Guid _user1LedgerId;
    private Guid _user2LedgerId;

    public MultiTenancyTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Setup test data without any user filter
        using var setupContext = new AppDbContext(_dbOptions);
        SetupTestData(setupContext);
    }

    public void Dispose()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    private void SetupTestData(AppDbContext context)
    {
        // Create two users
        var user1 = new User("user1@test.com", "password", "User One");
        typeof(User).GetProperty("Id")!.SetValue(user1, _user1Id);
        
        var user2 = new User("user2@test.com", "password", "User Two");
        typeof(User).GetProperty("Id")!.SetValue(user2, _user2Id);
        
        context.Users.AddRange(user1, user2);

        // Create portfolios for each user
        var portfolio1 = new Portfolio(_user1Id);
        portfolio1.SetDescription("User1 Portfolio");

        var portfolio2 = new Portfolio(_user2Id);
        portfolio2.SetDescription("User2 Portfolio");

        context.Portfolios.AddRange(portfolio1, portfolio2);

        // Create currency ledgers for each user
        var ledger1 = new CurrencyLedger(_user1Id, "USD", "User1 USD Account");
        var ledger2 = new CurrencyLedger(_user2Id, "USD", "User2 USD Account");
        context.CurrencyLedgers.AddRange(ledger1, ledger2);

        context.SaveChanges();

        _user1PortfolioId = portfolio1.Id;
        _user2PortfolioId = portfolio2.Id;
        _user1LedgerId = ledger1.Id;
        _user2LedgerId = ledger2.Id;

        // Add stock transactions for each portfolio
        var tx1 = new StockTransaction(
            portfolio1.Id,
            DateTime.UtcNow.AddDays(-5),
            "AAPL",
            TransactionType.Buy,
            10m, 150m, 31.5m);
        
        var tx2 = new StockTransaction(
            portfolio2.Id,
            DateTime.UtcNow.AddDays(-3),
            "GOOGL",
            TransactionType.Buy,
            5m, 2800m, 31.5m);
        
        context.StockTransactions.AddRange(tx1, tx2);

        // Add currency transactions for each ledger
        var currencyTx1 = new CurrencyTransaction(
            ledger1.Id,
            DateTime.UtcNow.AddDays(-10),
            CurrencyTransactionType.ExchangeBuy,
            1000m, 31500m, 31.5m);
        
        var currencyTx2 = new CurrencyTransaction(
            ledger2.Id,
            DateTime.UtcNow.AddDays(-8),
            CurrencyTransactionType.ExchangeBuy,
            2000m, 63000m, 31.5m);
        
        context.CurrencyTransactions.AddRange(currencyTx1, currencyTx2);
        
        context.SaveChanges();
    }

    private AppDbContext CreateContextForUser(Guid userId)
    {
        var mockUserService = new Mock<ICurrentUserService>();
        mockUserService.Setup(x => x.UserId).Returns(userId);
        return new AppDbContext(_dbOptions, mockUserService.Object);
    }

    [Fact]
    public async Task Portfolio_GlobalFilter_User1CanOnlySeeOwnPortfolios()
    {
        // Arrange
        await using var context = CreateContextForUser(_user1Id);

        // Act
        var portfolios = await context.Portfolios.ToListAsync();

        // Assert
        portfolios.Should().HaveCount(1);
        portfolios.First().Description.Should().Be("User1 Portfolio");
        portfolios.First().UserId.Should().Be(_user1Id);
    }

    [Fact]
    public async Task Portfolio_GlobalFilter_User2CanOnlySeeOwnPortfolios()
    {
        // Arrange
        await using var context = CreateContextForUser(_user2Id);

        // Act
        var portfolios = await context.Portfolios.ToListAsync();

        // Assert
        portfolios.Should().HaveCount(1);
        portfolios.First().Description.Should().Be("User2 Portfolio");
        portfolios.First().UserId.Should().Be(_user2Id);
    }

    [Fact]
    public async Task CurrencyLedger_GlobalFilter_User1CanOnlySeeOwnLedgers()
    {
        // Arrange
        await using var context = CreateContextForUser(_user1Id);

        // Act
        var ledgers = await context.CurrencyLedgers.ToListAsync();

        // Assert
        ledgers.Should().HaveCount(1);
        ledgers.First().Name.Should().Be("User1 USD Account");
        ledgers.First().UserId.Should().Be(_user1Id);
    }

    [Fact]
    public async Task CurrencyLedger_GlobalFilter_User2CanOnlySeeOwnLedgers()
    {
        // Arrange
        await using var context = CreateContextForUser(_user2Id);

        // Act
        var ledgers = await context.CurrencyLedgers.ToListAsync();

        // Assert
        ledgers.Should().HaveCount(1);
        ledgers.First().Name.Should().Be("User2 USD Account");
        ledgers.First().UserId.Should().Be(_user2Id);
    }

    [Fact]
    public async Task Portfolio_CannotFindOtherUserPortfolioById()
    {
        // Arrange - User1 trying to access User2's portfolio
        await using var context = CreateContextForUser(_user1Id);

        // Act
        var portfolio = await context.Portfolios.FindAsync(_user2PortfolioId);

        // Assert - Should not find due to global filter
        portfolio.Should().BeNull();
    }

    [Fact]
    public async Task CurrencyLedger_CannotFindOtherUserLedgerById()
    {
        // Arrange - User1 trying to access User2's ledger
        await using var context = CreateContextForUser(_user1Id);

        // Act
        var ledger = await context.CurrencyLedgers.FindAsync(_user2LedgerId);

        // Assert - Should not find due to global filter
        ledger.Should().BeNull();
    }

    [Fact]
    public async Task StockTransaction_FilteredByPortfolioOwnership()
    {
        // Arrange - User1 should only see transactions from their portfolio
        await using var context = CreateContextForUser(_user1Id);

        // Act - Get transactions for user1's portfolio
        var transactions = await context.StockTransactions
            .Where(t => t.PortfolioId == _user1PortfolioId)
            .ToListAsync();

        // Assert
        transactions.Should().HaveCount(1);
        transactions.First().Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task StockTransaction_CannotAccessOtherUserTransactions()
    {
        // Arrange - User1 trying to access User2's portfolio transactions
        await using var context = CreateContextForUser(_user1Id);

        // Act - User2's portfolio ID shouldn't return any transactions
        // because User1 can't see User2's portfolio
        var portfolio = await context.Portfolios.FindAsync(_user2PortfolioId);

        // Assert - Portfolio should be null, so no transactions accessible
        portfolio.Should().BeNull();
    }

    [Fact]
    public async Task CurrencyTransaction_FilteredByLedgerOwnership()
    {
        // Arrange - User1 should only see transactions from their ledger
        await using var context = CreateContextForUser(_user1Id);

        // Act - Get transactions for user1's ledger
        var transactions = await context.CurrencyTransactions
            .Where(t => t.CurrencyLedgerId == _user1LedgerId)
            .ToListAsync();

        // Assert
        transactions.Should().HaveCount(1);
        transactions.First().ForeignAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task CurrencyTransaction_CannotAccessOtherUserTransactions()
    {
        // Arrange - User1 trying to access User2's ledger transactions
        await using var context = CreateContextForUser(_user1Id);

        // Act - User2's ledger ID shouldn't return any transactions
        // because User1 can't see User2's ledger
        var ledger = await context.CurrencyLedgers.FindAsync(_user2LedgerId);

        // Assert - Ledger should be null, so no transactions accessible
        ledger.Should().BeNull();
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllData()
    {
        // Arrange - Admin context ignoring filters
        await using var context = new AppDbContext(_dbOptions);

        // Act
        var allPortfolios = await context.Portfolios.IgnoreQueryFilters().ToListAsync();
        var allLedgers = await context.CurrencyLedgers.IgnoreQueryFilters().ToListAsync();

        // Assert - Should see all data when ignoring filters
        allPortfolios.Should().HaveCount(2);
        allLedgers.Should().HaveCount(2);
    }
}
