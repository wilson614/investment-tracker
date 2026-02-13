using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Application.Validators;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Integration tests for atomic stock purchase with currency ledger deduction.
/// Updated for Story 0.2: Portfolio-Ledger 1:1 binding model.
/// </summary>
public class AtomicTransactionTests : IDisposable
{
    private readonly SqliteConnection _connection;
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
    private readonly IAppDbTransactionManager _transactionManager;
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly string _testJwtToken;

    public AtomicTransactionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(_testUserId);

        _testJwtToken = GenerateTestToken(_testUserId);

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
        _dbContext.Database.EnsureCreated();

        _portfolioRepository = new PortfolioRepository(_dbContext);
        _stockTransactionRepository = new StockTransactionRepository(_dbContext);
        _currencyLedgerRepository = new CurrencyLedgerRepository(_dbContext);
        _currencyTransactionRepository = new CurrencyTransactionRepository(_dbContext);
        _portfolioCalculator = new PortfolioCalculator();
        _currencyLedgerService = new CurrencyLedgerService();
        _transactionManager = new AppDbTransactionManager(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private static string GenerateTestToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, $"test-{userId:N}@example.com"),
            new Claim(ClaimTypes.Name, "Atomic Transaction Test User")
        };

        var handler = new JsonWebTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "InvestmentTracker",
            Audience = "InvestmentTracker",
            SigningCredentials = credentials
        };

        return handler.CreateToken(tokenDescriptor);
    }

    private async Task EnsureApiUserExistsAsync(Guid userId)
    {
        var existing = await _dbContext.Users.FindAsync(userId);
        if (existing is not null)
            return;

        var user = new User($"api-{userId:N}@example.com", "password", "API Test User");
        typeof(User).GetProperty("Id")!.SetValue(user, userId);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
    }

    private static readonly JsonSerializerOptions _apiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: true)
        }
    };

    private static async Task<PortfolioDto> CreateTestPortfolioViaApiAsync(HttpClient client, string currencyCode, string displayName)
    {
        var response = await client.PostAsJsonAsync("/api/portfolios", new
        {
            CurrencyCode = currencyCode,
            DisplayName = displayName
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<PortfolioDto>(payload, _apiJsonOptions);
        dto.Should().NotBeNull();
        return dto;
    }

    private HttpClient CreateAuthorizedApiClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testJwtToken);
        return client;
    }

    private static async Task<T?> ReadApiJsonAsync<T>(HttpContent content)
    {
        var payload = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(payload, _apiJsonOptions);
    }

    private async Task<(Portfolio portfolio, CurrencyLedger ledger)> SetupTestDataAsync()
    {
        // Create user
        var user = new User($"test-{Guid.NewGuid():N}@example.com", "password", "Test User");
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

    private async Task<(Portfolio portfolio, CurrencyLedger ledger, CurrencyTransaction transaction)> SetupCurrencyTransactionForUpdateAsync()
    {
        var (portfolio, ledger) = await SetupTestDataAsync();

        var transaction = new CurrencyTransaction(
            ledger.Id,
            DateTime.UtcNow.AddDays(-2),
            CurrencyTransactionType.Deposit,
            foreignAmount: 120m,
            homeAmount: 3780m,
            exchangeRate: 31.5m,
            notes: "update-target");

        await _currencyTransactionRepository.AddAsync(transaction);

        return (portfolio, ledger, transaction);
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
        incomeTx.ForeignAmount.Should().Be(295m); // 5*60 - 5 fees
    }

    [Fact]
    public async Task CreateCurrencyTransaction_WhenSnapshotUpsertFails_ShouldRollbackAndNotPersistTransaction()
    {
        // Arrange
        var (portfolio, ledger) = await SetupTestDataAsync();

        _txSnapshotServiceMock
            .Setup(x => x.UpsertSnapshotAsync(
                portfolio.Id,
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("snapshot update failed"));

        var useCase = new CreateCurrencyTransactionUseCase(
            _currencyTransactionRepository,
            _currencyLedgerRepository,
            _portfolioRepository,
            _txSnapshotServiceMock.Object,
            _currentUserServiceMock.Object,
            _transactionManager);

        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = ledger.Id,
            TransactionDate = DateTime.UtcNow,
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 50m,
            HomeAmount = 1575m,
            ExchangeRate = 31.5m,
            Notes = "should rollback"
        };

        // Act
        var act = async () => await useCase.ExecuteAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("snapshot update failed");

        var transactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        transactions.Should().HaveCount(1, "snapshot upsert failure should rollback the newly created deposit transaction");
        transactions.Should().OnlyContain(t => t.TransactionType == CurrencyTransactionType.ExchangeBuy);
    }

    [Fact]
    public async Task UpdateCurrencyTransaction_WhenSnapshotUpsertFails_ShouldRollbackAndPreserveOriginalValues()
    {
        // Arrange
        var (portfolio, ledger, transaction) = await SetupCurrencyTransactionForUpdateAsync();
        var originalDate = transaction.TransactionDate;
        var originalType = transaction.TransactionType;
        var originalAmount = transaction.ForeignAmount;
        var originalNotes = transaction.Notes;

        _txSnapshotServiceMock
            .Setup(x => x.UpsertSnapshotAsync(
                portfolio.Id,
                transaction.Id,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("snapshot update failed"));

        var useCase = new UpdateCurrencyTransactionUseCase(
            _currencyTransactionRepository,
            _currencyLedgerRepository,
            _portfolioRepository,
            _txSnapshotServiceMock.Object,
            _currentUserServiceMock.Object,
            _transactionManager);

        var request = new UpdateCurrencyTransactionRequest
        {
            TransactionDate = originalDate.AddDays(1),
            TransactionType = CurrencyTransactionType.Withdraw,
            ForeignAmount = 80m,
            HomeAmount = 2520m,
            ExchangeRate = 31.5m,
            Notes = "should rollback update"
        };

        // Act
        var act = async () => await useCase.ExecuteAsync(transaction.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("snapshot update failed");

        _dbContext.ChangeTracker.Clear();

        var reloaded = await _currencyTransactionRepository.GetByIdAsync(transaction.Id);
        reloaded.Should().NotBeNull();
        reloaded.TransactionDate.Should().Be(originalDate);
        reloaded.TransactionType.Should().Be(originalType);
        reloaded.ForeignAmount.Should().Be(originalAmount);
        reloaded.Notes.Should().Be(originalNotes);

        var ledgerTransactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(ledger.Id);
        ledgerTransactions.Should().HaveCount(2, "update failure should not create or delete extra transactions");
    }

    [Theory]
    [InlineData(CurrencyTransactionType.ExchangeBuy)]
    [InlineData(CurrencyTransactionType.ExchangeSell)]
    public void CreateCurrencyTransactionRequest_MissingExchangeRate_ForExchangeType_ShouldFailValidation(
        CurrencyTransactionType transactionType)
    {
        // Arrange
        var validator = new CreateCurrencyTransactionRequestValidator();
        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = Guid.NewGuid(),
            TransactionDate = DateTime.UtcNow,
            TransactionType = transactionType,
            ForeignAmount = 1000m,
            HomeAmount = 31500m,
            ExchangeRate = null
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateCurrencyTransactionRequest.ExchangeRate) &&
            e.ErrorMessage == "Exchange rate is required for exchange transactions");
    }

    [Fact]
    public async Task CurrencyTransactions_CreateApi_OnTwdLedger_WithExchangeBuy_ShouldReturnBadRequest()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "TWD", "TWD Create Validation");

        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.ExchangeBuy,
            ForeignAmount = 100m,
            HomeAmount = 3100m,
            ExchangeRate = 31m,
            Notes = "invalid for twd"
        };

        var response = await client.PostAsJsonAsync("/api/currencytransactions", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using (var json = JsonDocument.Parse(body))
        {
            var error = json.RootElement.GetProperty("error").GetString();
            error.Should().Contain("交易類型不符合此帳本規則");
            error.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");
        }

        var listResponse = await client.GetAsync($"/api/currencytransactions/ledger/{portfolio.BoundCurrencyLedgerId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transactions = await ReadApiJsonAsync<List<CurrencyTransactionDto>>(listResponse.Content);
        transactions.Should().NotBeNull();
        transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrencyTransactions_CreateApi_OnTwdLedger_WithDeposit_ShouldReturnCreatedAndForceRateOne()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "TWD", "TWD Create Valid");

        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 888m,
            HomeAmount = 9999m,
            ExchangeRate = 99m,
            Notes = "valid create"
        };

        var response = await client.PostAsJsonAsync("/api/currencytransactions", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await ReadApiJsonAsync<CurrencyTransactionDto>(response.Content);
        created.Should().NotBeNull();
        created.TransactionType.Should().Be(CurrencyTransactionType.Deposit);
        created.ForeignAmount.Should().Be(888m);
        created.HomeAmount.Should().Be(888m);
        created.ExchangeRate.Should().Be(1.0m);

        var listResponse = await client.GetAsync($"/api/currencytransactions/ledger/{portfolio.BoundCurrencyLedgerId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transactions = await ReadApiJsonAsync<List<CurrencyTransactionDto>>(listResponse.Content);
        transactions.Should().NotBeNull();
        transactions.Should().ContainSingle(t => t.Id == created.Id);
    }

    [Fact]
    public async Task CurrencyTransactions_UpdateApi_OnTwdLedger_InvalidThenValid_ShouldMatchValidationMatrix()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "TWD", "TWD Update Validation");

        var createRequest = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-2),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 300m,
            Notes = "seed"
        };

        var createResponse = await client.PostAsJsonAsync("/api/currencytransactions", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadApiJsonAsync<CurrencyTransactionDto>(createResponse.Content);
        created.Should().NotBeNull();

        var invalidUpdate = new UpdateCurrencyTransactionRequest
        {
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.ExchangeSell,
            ForeignAmount = 10m,
            HomeAmount = 310m,
            ExchangeRate = 31m,
            Notes = "invalid update"
        };

        var invalidResponse = await client.PutAsJsonAsync($"/api/currencytransactions/{created.Id}", invalidUpdate);

        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidBody = await invalidResponse.Content.ReadAsStringAsync();
        using (var json = JsonDocument.Parse(invalidBody))
        {
            var error = json.RootElement.GetProperty("error").GetString();
            error.Should().Contain("交易類型不符合此帳本規則");
            error.Should().Contain("TWD 帳本不可使用 ExchangeBuy/ExchangeSell");
        }

        var validUpdate = new UpdateCurrencyTransactionRequest
        {
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 777m,
            HomeAmount = 1m,
            ExchangeRate = 99m,
            Notes = "valid update"
        };

        var validResponse = await client.PutAsJsonAsync($"/api/currencytransactions/{created.Id}", validUpdate);

        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadApiJsonAsync<CurrencyTransactionDto>(validResponse.Content);
        updated.Should().NotBeNull();
        updated.ForeignAmount.Should().Be(777m);
        updated.HomeAmount.Should().Be(777m);
        updated.ExchangeRate.Should().Be(1.0m);
    }

    [Theory]
    [InlineData("TransferInBalance")]
    [InlineData("Dividend")]
    [InlineData("StockBuy")]
    [InlineData("StockSell")]
    public async Task CurrencyTransactions_CreateApi_WithDeprecatedEnumName_ShouldReturnBadRequest(string legacyEnumName)
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "USD", "Legacy Enum Create");

        var payload = $$"""
{
  "currencyLedgerId": "{{portfolio.BoundCurrencyLedgerId}}",
  "transactionDate": "{{DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}}",
  "transactionType": "{{legacyEnumName}}",
  "foreignAmount": 100,
  "homeAmount": 3100,
  "exchangeRate": 31,
  "notes": "legacy enum create"
}
""";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/currencytransactions", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().ContainAny(
            "could not be converted",
            "The JSON value could not be converted",
            "One or more validation errors occurred");
        body.Should().Contain("transactionType");
    }

    [Theory]
    [InlineData("TransferInBalance")]
    [InlineData("Dividend")]
    [InlineData("StockBuy")]
    [InlineData("StockSell")]
    public async Task CurrencyTransactions_UpdateApi_WithDeprecatedEnumName_ShouldReturnBadRequest(string legacyEnumName)
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "USD", "Legacy Enum Update");

        var createRequest = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-2),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 100m,
            Notes = "seed for legacy update"
        };

        var createResponse = await client.PostAsJsonAsync("/api/currencytransactions", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadApiJsonAsync<CurrencyTransactionDto>(createResponse.Content);
        created.Should().NotBeNull();

        var payload = $$"""
{
  "transactionDate": "{{DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}}",
  "transactionType": "{{legacyEnumName}}",
  "foreignAmount": 120,
  "homeAmount": 3720,
  "exchangeRate": 31,
  "notes": "legacy enum update"
}
""";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PutAsync($"/api/currencytransactions/{created.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().ContainAny(
            "could not be converted",
            "The JSON value could not be converted",
            "One or more validation errors occurred");
        body.Should().Contain("transactionType");
    }

    [Fact]
    public async Task CurrencyTransactions_ImportCsv_WhenAnyInvalidRows_ShouldReturn422WithFullErrorSetAndNoCommit()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "TWD", "CSV Invalid Import");

        const string csv = """
transactionDate,transactionType,foreignAmount,homeAmount,exchangeRate,notes
2026-01-01,ExchangeBuy,100,3100,31,invalid type for twd
2026-01-02,,0,,abc,
""";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(portfolio.BoundCurrencyLedgerId.ToString()), "ledgerId");
        form.Add(new StringContent(csv, Encoding.UTF8), "file", "invalid.csv");

        var response = await client.PostAsync("/api/currencytransactions/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var result = await ReadApiJsonAsync<CurrencyTransactionCsvImportResultDto>(response.Content);
        result.Should().NotBeNull();
        result.Status.Should().Be("rejected");
        result.Summary.TotalRows.Should().Be(2);
        result.Summary.InsertedRows.Should().Be(0);
        result.Summary.RejectedRows.Should().Be(2);
        result.Summary.ErrorCount.Should().NotBeNull();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(4);

        result.Errors.Should().Contain(e =>
            e.RowNumber == 2 &&
            e.FieldName == "transactionType" &&
            e.ErrorCode == "INVALID_TRANSACTION_TYPE_FOR_LEDGER");

        result.Errors.Should().Contain(e =>
            e.RowNumber == 3 &&
            e.FieldName == "transactionType" &&
            e.ErrorCode == "REQUIRED_FIELD_MISSING");

        result.Errors.Should().Contain(e =>
            e.RowNumber == 3 &&
            e.FieldName == "foreignAmount" &&
            e.ErrorCode == "VALUE_OUT_OF_RANGE");

        result.Errors.Should().Contain(e =>
            e.RowNumber == 3 &&
            e.FieldName == "exchangeRate" &&
            e.ErrorCode == "INVALID_NUMBER_FORMAT");

        var listResponse = await client.GetAsync($"/api/currencytransactions/ledger/{portfolio.BoundCurrencyLedgerId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transactions = await ReadApiJsonAsync<List<CurrencyTransactionDto>>(listResponse.Content);
        transactions.Should().NotBeNull();
        transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrencyTransactions_ImportCsv_ValidRows_ShouldCommitAllRows()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "USD", "CSV Valid Import");

        const string csv = """
transactionDate,transactionType,foreignAmount,homeAmount,exchangeRate,notes
2026-01-01,ExchangeBuy,100,3100,31,fx in
2026-01-02,Deposit,50,,,cash in
""";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(portfolio.BoundCurrencyLedgerId.ToString()), "ledgerId");
        form.Add(new StringContent(csv, Encoding.UTF8), "file", "valid.csv");

        var response = await client.PostAsync("/api/currencytransactions/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiJsonAsync<CurrencyTransactionCsvImportResultDto>(response.Content);
        result.Should().NotBeNull();
        result.Status.Should().Be("committed");
        result.Summary.TotalRows.Should().Be(2);
        result.Summary.InsertedRows.Should().Be(2);
        result.Summary.RejectedRows.Should().Be(0);
        result.Errors.Should().BeEmpty();

        var listResponse = await client.GetAsync($"/api/currencytransactions/ledger/{portfolio.BoundCurrencyLedgerId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transactions = await ReadApiJsonAsync<List<CurrencyTransactionDto>>(listResponse.Content);
        transactions.Should().NotBeNull();
        transactions.Should().HaveCount(2);
        transactions.Should().Contain(t => t.TransactionType == CurrencyTransactionType.ExchangeBuy && t.ForeignAmount == 100m);
        transactions.Should().Contain(t => t.TransactionType == CurrencyTransactionType.Deposit && t.ForeignAmount == 50m);
    }

    [Fact]
    public async Task CurrencyTransactions_ImportCsv_RejectedDiagnostics_ShouldContainFixedSchemaFields()
    {
        var apiUserId = _testUserId;
        await EnsureApiUserExistsAsync(apiUserId);

        await using var factory = new CustomWebApplicationFactory
        {
            TestUserId = apiUserId
        };

        using var client = CreateAuthorizedApiClient(factory);

        var portfolio = await CreateTestPortfolioViaApiAsync(client, "USD", "CSV Diagnostics Schema");

        const string csv = """
transactionDate,transactionType,foreignAmount,homeAmount,exchangeRate,notes
bad-date,UnknownType,abc,,,
""";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(portfolio.BoundCurrencyLedgerId.ToString()), "ledgerId");
        form.Add(new StringContent(csv, Encoding.UTF8), "file", "schema.csv");

        var response = await client.PostAsync("/api/currencytransactions/import", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        var errors = json.RootElement.GetProperty("errors");
        errors.ValueKind.Should().Be(JsonValueKind.Array);
        errors.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var error in errors.EnumerateArray())
        {
            error.TryGetProperty("rowNumber", out var row).Should().BeTrue();
            row.ValueKind.Should().Be(JsonValueKind.Number);

            error.TryGetProperty("fieldName", out var field).Should().BeTrue();
            field.ValueKind.Should().Be(JsonValueKind.String);
            field.GetString().Should().NotBeNullOrWhiteSpace();

            error.TryGetProperty("invalidValue", out var value).Should().BeTrue();
            (value.ValueKind == JsonValueKind.String || value.ValueKind == JsonValueKind.Null).Should().BeTrue();

            error.TryGetProperty("correctionGuidance", out var guidance).Should().BeTrue();
            guidance.ValueKind.Should().Be(JsonValueKind.String);
            guidance.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

}
