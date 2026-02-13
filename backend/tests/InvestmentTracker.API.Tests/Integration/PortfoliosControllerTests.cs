using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Integration tests for PortfoliosController.
/// Tests the full HTTP request/response cycle including middleware.
/// </summary>
public class PortfoliosControllerTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoPortfolios()
    {
        // Act
        var response = await Client.GetAsync("/api/portfolios");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var portfolios = await response.Content.ReadFromJsonAsync<List<PortfolioDto>>();
        portfolios.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ReturnsCreatedPortfolio()
    {
        // Arrange
        var request = new { Description = "My Test Portfolio", CurrencyCode = "USD" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/portfolios", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();
        portfolio.Description.Should().Be("My Test Portfolio");
        portfolio.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetById_ReturnsPortfolio_WhenExists()
    {
        // Arrange
        var created = await CreateTestPortfolioAsync("Get By Id Test");

        // Act
        var response = await Client.GetAsync($"/api/portfolios/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();
        portfolio.Id.Should().Be(created.Id);
        portfolio.Description.Should().Be("Get By Id Test");
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await Client.GetAsync($"/api/portfolios/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedPortfolio()
    {
        // Arrange
        var created = await CreateTestPortfolioAsync("Original");
        var updateRequest = new { Description = "Updated Description" };

        // Act
        var response = await Client.PutAsJsonAsync($"/api/portfolios/{created.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        updated.Should().NotBeNull();
        updated.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        var updateRequest = new { Description = "Updated" };

        // Act
        var response = await Client.PutAsJsonAsync($"/api/portfolios/{Guid.NewGuid()}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var created = await CreateTestPortfolioAsync("To Delete");

        // Act
        var response = await Client.DeleteAsync($"/api/portfolios/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await Client.GetAsync($"/api/portfolios/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await Client.DeleteAsync($"/api/portfolios/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateCurrencyTransaction_WithRelatedStockTransactionId_ShouldReturnBadRequest()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Currency Tx Invalid RelatedStockTransactionId");
        var request = new CreateCurrencyTransactionRequest
        {
            CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            TransactionType = CurrencyTransactionType.Deposit,
            ForeignAmount = 1000m,
            RelatedStockTransactionId = Guid.NewGuid(),
            Notes = "should fail"
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/currencytransactions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RelatedStockTransactionId cannot be provided when creating currency transactions.");
    }

    [Fact]
    public async Task Unauthorized_WhenNoToken()
    {
        // Arrange
        using var clientWithoutAuth = Factory.CreateClient();

        // Act
        var response = await clientWithoutAuth.GetAsync("/api/portfolios");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAggregateAvailableYears_ReturnsEmptyDto_WhenNoPortfolios()
    {
        // Act
        var response = await Client.GetAsync("/api/portfolios/aggregate/performance/years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AvailableYearsDto>();
        result.Should().NotBeNull();
        result!.Years.Should().BeEmpty();
        result.EarliestYear.Should().BeNull();
        result.CurrentYear.Should().Be(DateTime.UtcNow.Year);
    }

    [Fact]
    public async Task GetAggregateAvailableYears_ReturnsEmptyDto_WhenPortfoliosHaveNoTransactions()
    {
        // Arrange
        await CreateTestPortfolioWithCurrencyAsync("No Tx A", "USD");
        await CreateTestPortfolioWithCurrencyAsync("No Tx B", "EUR");

        // Act
        var response = await Client.GetAsync("/api/portfolios/aggregate/performance/years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AvailableYearsDto>();
        result.Should().NotBeNull();
        result!.Years.Should().BeEmpty();
        result.EarliestYear.Should().BeNull();
        result.CurrentYear.Should().Be(DateTime.UtcNow.Year);
    }

    [Fact]
    public async Task GetAggregateAvailableYears_ReturnsYears_WhenTransactionsExist()
    {
        // Arrange
        var portfolioA = await CreateTestPortfolioWithCurrencyAsync("With Tx A", "USD");
        var portfolioB = await CreateTestPortfolioWithCurrencyAsync("With Tx B", "EUR");

        var txA = new CreateStockTransactionRequest
        {
            PortfolioId = portfolioA.Id,
            TransactionDate = new DateTime(2022, 5, 2),
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 3,
            PricePerShare = 100,
            Fees = 0,
            Market = StockMarket.US,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.Margin
        };

        var txB = new CreateStockTransactionRequest
        {
            PortfolioId = portfolioB.Id,
            TransactionDate = new DateTime(2024, 7, 10),
            Ticker = "VWCE.DE",
            TransactionType = TransactionType.Buy,
            Shares = 2,
            PricePerShare = 90,
            Fees = 0,
            Market = StockMarket.EU,
            Currency = Currency.EUR,
            BalanceAction = BalanceAction.Margin
        };

        (await Client.PostAsJsonAsync("/api/stocktransactions", txA)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Client.PostAsJsonAsync("/api/stocktransactions", txB)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var response = await Client.GetAsync("/api/portfolios/aggregate/performance/years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AvailableYearsDto>();
        result.Should().NotBeNull();
        result!.EarliestYear.Should().Be(2022);

        var currentYear = DateTime.UtcNow.Year;
        var expectedYears = Enumerable.Range(2022, currentYear - 2022 + 1)
            .OrderDescending()
            .ToList();

        result.CurrentYear.Should().Be(currentYear);
        result.Years.Should().Equal(expectedYears);
    }

    [Fact]
    public async Task GetAggregateAvailableYears_WithOnlyOnePortfolioHavingTransactions_MatchesThatPortfolioYears()
    {
        // Arrange
        var targetYear = DateTime.UtcNow.Year - 1;
        var usdPortfolio = await CreateTestPortfolioWithCurrencyAsync("USD Active", "USD");
        await CreateTestPortfolioWithCurrencyAsync("TWD Empty", "TWD");

        var tx = new CreateStockTransactionRequest
        {
            PortfolioId = usdPortfolio.Id,
            TransactionDate = new DateTime(targetYear, 4, 20),
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 1,
            PricePerShare = 100,
            Fees = 0,
            Market = StockMarket.US,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.Margin
        };

        (await Client.PostAsJsonAsync("/api/stocktransactions", tx)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var aggregateResponse = await Client.GetAsync("/api/portfolios/aggregate/performance/years");
        var singleResponse = await Client.GetAsync($"/api/portfolios/{usdPortfolio.Id}/performance/years");

        // Assert
        aggregateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        singleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<AvailableYearsDto>();
        var single = await singleResponse.Content.ReadFromJsonAsync<AvailableYearsDto>();

        aggregate.Should().NotBeNull();
        single.Should().NotBeNull();

        aggregate!.EarliestYear.Should().Be(single!.EarliestYear);
        aggregate.CurrentYear.Should().Be(single.CurrentYear);
        aggregate.Years.Should().Equal(single.Years);
    }

    private async Task<PortfolioDto> CreateTestPortfolioWithCurrencyAsync(string displayName, string currencyCode)
    {
        var request = new
        {
            CurrencyCode = currencyCode,
            DisplayName = displayName
        };

        var response = await Client.PostAsJsonAsync("/api/portfolios", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();
        return portfolio!;
    }

    private async Task<YearPerformanceDto> CalculateAggregateYearPerformanceAsync(int year)
    {
        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 110m, ExchangeRate = 31m }
            },
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 95m, ExchangeRate = 30m }
            }
        };

        return await CalculateAggregateYearPerformanceAsync(request);
    }

    private async Task<YearPerformanceDto> CalculateAggregateYearPerformanceAsync(CalculateYearPerformanceRequest request)
    {
        var response = await Client.PostAsJsonAsync("/api/portfolios/aggregate/performance/year", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<YearPerformanceDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    private async Task<YearPerformanceDto> CalculatePortfolioYearPerformanceAsync(Guid portfolioId, int year)
    {
        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 110m, ExchangeRate = 31m }
            },
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 95m, ExchangeRate = 30m }
            }
        };

        return await CalculatePortfolioYearPerformanceAsync(portfolioId, request);
    }

    private async Task<YearPerformanceDto> CalculatePortfolioYearPerformanceAsync(
        Guid portfolioId,
        CalculateYearPerformanceRequest request)
    {
        var response = await Client.PostAsJsonAsync($"/api/portfolios/{portfolioId}/performance/year", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<YearPerformanceDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    private async Task AddInitialDepositAsync(
        Guid portfolioId,
        DateTime date,
        decimal amount,
        decimal homeAmount,
        decimal exchangeRate)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var portfolio = await db.Portfolios
            .IgnoreQueryFilters()
            .OfType<Portfolio>()
            .SingleAsync(p => p.Id == portfolioId);

        var response = await Client.PostAsJsonAsync(
            "/api/currencytransactions",
            new CreateCurrencyTransactionRequest
            {
                CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
                TransactionDate = date,
                TransactionType = CurrencyTransactionType.InitialBalance,
                ForeignAmount = amount,
                HomeAmount = homeAmount,
                ExchangeRate = exchangeRate,
                Notes = "integration-test initial deposit"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private Task AddInitialUsdDepositAsync(Guid portfolioId, DateTime date, decimal amount)
    {
        return AddInitialDepositAsync(
            portfolioId,
            date,
            amount,
            homeAmount: amount * 30m,
            exchangeRate: 30m);
    }

    private async Task AddDepositAsync(
        Guid portfolioId,
        DateTime date,
        decimal amount,
        decimal homeAmount,
        decimal exchangeRate)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var portfolio = await db.Portfolios
            .IgnoreQueryFilters()
            .OfType<Portfolio>()
            .SingleAsync(p => p.Id == portfolioId);

        var response = await Client.PostAsJsonAsync(
            "/api/currencytransactions",
            new CreateCurrencyTransactionRequest
            {
                CurrencyLedgerId = portfolio.BoundCurrencyLedgerId,
                TransactionDate = date,
                TransactionType = CurrencyTransactionType.Deposit,
                ForeignAmount = amount,
                HomeAmount = homeAmount,
                ExchangeRate = exchangeRate,
                Notes = "integration-test deposit"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AggregatePerformance_WithOnlyOneActivePortfolio_MatchesSinglePortfolioCurrencyAndValues()
    {
        // Arrange
        var targetYear = DateTime.UtcNow.Year - 1;

        var usdPortfolio = await CreateTestPortfolioWithCurrencyAsync("USD Active", "USD");
        await CreateTestPortfolioWithCurrencyAsync("TWD Empty", "TWD");

        await AddInitialUsdDepositAsync(usdPortfolio.Id, new DateTime(targetYear, 1, 5), 1000m);

        var usdBuyRequest = new CreateStockTransactionRequest
        {
            PortfolioId = usdPortfolio.Id,
            TransactionDate = new DateTime(targetYear, 3, 15),
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 1m,
            PricePerShare = 100m,
            Fees = 0m,
            Market = StockMarket.US,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.None
        };

        var createTxResponse = await Client.PostAsJsonAsync("/api/stocktransactions", usdBuyRequest);
        createTxResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var aggregate = await CalculateAggregateYearPerformanceAsync(targetYear);
        var single = await CalculatePortfolioYearPerformanceAsync(usdPortfolio.Id, targetYear);

        // Assert
        aggregate.SourceCurrency.Should().Be(single.SourceCurrency);
        aggregate.SourceCurrency.Should().Be("USD");

        aggregate.StartValueHome.Should().Be(single.StartValueHome);
        aggregate.EndValueHome.Should().Be(single.EndValueHome);
        aggregate.NetContributionsHome.Should().Be(single.NetContributionsHome);
        aggregate.StartValueSource.Should().Be(single.StartValueSource);
        aggregate.EndValueSource.Should().Be(single.EndValueSource);
        aggregate.NetContributionsSource.Should().Be(single.NetContributionsSource);

        aggregate.TotalReturnPercentage.Should().Be(single.TotalReturnPercentage);
        aggregate.TotalReturnPercentageSource.Should().Be(single.TotalReturnPercentageSource);
        aggregate.ModifiedDietzPercentage.Should().Be(single.ModifiedDietzPercentage);
        aggregate.ModifiedDietzPercentageSource.Should().Be(single.ModifiedDietzPercentageSource);
        aggregate.TimeWeightedReturnPercentage.Should().Be(single.TimeWeightedReturnPercentage);
        aggregate.TimeWeightedReturnPercentageSource.Should().Be(single.TimeWeightedReturnPercentageSource);

        aggregate.TransactionCount.Should().Be(single.TransactionCount);
        aggregate.CashFlowCount.Should().Be(single.CashFlowCount);
        aggregate.EarliestTransactionDateInYear.Should().Be(single.EarliestTransactionDateInYear);
        aggregate.MissingPrices.Should().BeEmpty();
    }

    [Fact]
    public async Task AggregatePerformance_WithUsdAndTwdActivePortfolios_ReconcilesHomeMetricsWithSingles()
    {
        // Arrange
        const int targetYear = 2024;
        const int previousYear = targetYear - 1;

        var usdPortfolio = await CreateTestPortfolioWithCurrencyAsync("USD Active", "USD");
        var twdPortfolio = await CreateTestPortfolioWithCurrencyAsync("TWD Active", "TWD");

        await AddInitialUsdDepositAsync(usdPortfolio.Id, new DateTime(previousYear, 12, 20), 3000m);
        await AddInitialDepositAsync(
            twdPortfolio.Id,
            new DateTime(previousYear, 12, 20),
            amount: 120000m,
            homeAmount: 120000m,
            exchangeRate: 1m);

        var transactions = new[]
        {
            new CreateStockTransactionRequest
            {
                PortfolioId = usdPortfolio.Id,
                TransactionDate = new DateTime(previousYear, 11, 15),
                Ticker = "AAPL",
                TransactionType = TransactionType.Buy,
                Shares = 2m,
                PricePerShare = 90m,
                Fees = 0m,
                Market = StockMarket.US,
                Currency = Currency.USD,
                BalanceAction = BalanceAction.None
            },
            new CreateStockTransactionRequest
            {
                PortfolioId = usdPortfolio.Id,
                TransactionDate = new DateTime(targetYear, 3, 10),
                Ticker = "AAPL",
                TransactionType = TransactionType.Buy,
                Shares = 1m,
                PricePerShare = 100m,
                Fees = 0m,
                Market = StockMarket.US,
                Currency = Currency.USD,
                BalanceAction = BalanceAction.None
            },
            new CreateStockTransactionRequest
            {
                PortfolioId = twdPortfolio.Id,
                TransactionDate = new DateTime(previousYear, 11, 10),
                Ticker = "2330",
                TransactionType = TransactionType.Buy,
                Shares = 10m,
                PricePerShare = 500m,
                Fees = 0m,
                Market = StockMarket.TW,
                Currency = Currency.TWD,
                BalanceAction = BalanceAction.None
            },
            new CreateStockTransactionRequest
            {
                PortfolioId = twdPortfolio.Id,
                TransactionDate = new DateTime(targetYear, 6, 18),
                Ticker = "2330",
                TransactionType = TransactionType.Buy,
                Shares = 5m,
                PricePerShare = 600m,
                Fees = 0m,
                Market = StockMarket.TW,
                Currency = Currency.TWD,
                BalanceAction = BalanceAction.None
            }
        };

        foreach (var tx in transactions)
        {
            var txResponse = await Client.PostAsJsonAsync("/api/stocktransactions", tx);
            txResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var request = new CalculateYearPerformanceRequest
        {
            Year = targetYear,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 110m, ExchangeRate = 32m },
                ["2330"] = new() { Price = 650m, ExchangeRate = 1m }
            },
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 95m, ExchangeRate = 30m },
                ["2330"] = new() { Price = 520m, ExchangeRate = 1m }
            }
        };

        // Act
        var aggregate = await CalculateAggregateYearPerformanceAsync(request);
        var usdSingle = await CalculatePortfolioYearPerformanceAsync(usdPortfolio.Id, request);
        var twdSingle = await CalculatePortfolioYearPerformanceAsync(twdPortfolio.Id, request);

        var expectedStartValueHome = (usdSingle.StartValueHome ?? 0m) + (twdSingle.StartValueHome ?? 0m);
        var expectedEndValueHome = (usdSingle.EndValueHome ?? 0m) + (twdSingle.EndValueHome ?? 0m);
        var expectedNetContributionsHome = usdSingle.NetContributionsHome + twdSingle.NetContributionsHome;
        var expectedEarliestTransaction = new[]
        {
            usdSingle.EarliestTransactionDateInYear,
            twdSingle.EarliestTransactionDateInYear
        }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Min();

        // Assert
        usdSingle.NetContributionsHome.Should().BeGreaterThan(0m);
        twdSingle.NetContributionsHome.Should().BeGreaterThan(0m);

        aggregate.MissingPrices.Should().BeEmpty();
        aggregate.Year.Should().Be(targetYear);

        aggregate.StartValueHome.Should().Be(expectedStartValueHome);
        aggregate.EndValueHome.Should().Be(expectedEndValueHome);
        aggregate.NetContributionsHome.Should().Be(expectedNetContributionsHome);

        aggregate.TransactionCount.Should().Be(usdSingle.TransactionCount + twdSingle.TransactionCount);
        aggregate.CashFlowCount.Should().Be(usdSingle.CashFlowCount + twdSingle.CashFlowCount);
        aggregate.EarliestTransactionDateInYear.Should().Be(expectedEarliestTransaction);

        aggregate.EndValueHome.Should().BeGreaterThan(usdSingle.EndValueHome ?? 0m);
        aggregate.EndValueHome.Should().BeGreaterThan(twdSingle.EndValueHome ?? 0m);
        aggregate.NetContributionsHome.Should().BeGreaterThan(usdSingle.NetContributionsHome);
        aggregate.NetContributionsHome.Should().BeGreaterThan(twdSingle.NetContributionsHome);
    }

    [Fact]
    public async Task AggregatePerformance_WithMidYearContribution_BaselineParityMaintainsMdTwrDifferentialLikeSinglePortfolio()
    {
        // Arrange
        const int targetYear = 2024;
        var activePortfolio = await CreateTestPortfolioWithCurrencyAsync("USD Baseline Parity", "USD");
        await CreateTestPortfolioWithCurrencyAsync("TWD Empty", "TWD");

        // Ensure aggregate path executes with one active + one inactive portfolio
        // and source/home remain numerically identical (USD home/base).
        await AddInitialDepositAsync(
            activePortfolio.Id,
            new DateTime(targetYear - 1, 12, 20),
            amount: 1500m,
            homeAmount: 1500m,
            exchangeRate: 1m);

        // Build year-start stock position (value = 1000) with remaining cash in ledger (500).
        var buyBeforeYear = new CreateStockTransactionRequest
        {
            PortfolioId = activePortfolio.Id,
            TransactionDate = new DateTime(targetYear - 1, 12, 25),
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 10m,
            PricePerShare = 100m,
            Fees = 0m,
            Market = StockMarket.US,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.None
        };

        var buyResponse = await Client.PostAsJsonAsync("/api/stocktransactions", buyBeforeYear);
        buyResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Add explicit external contribution in-year to force MD/TWR differential.
        await AddDepositAsync(
            activePortfolio.Id,
            new DateTime(targetYear, 6, 30),
            amount: 100m,
            homeAmount: 100m,
            exchangeRate: 1m);

        var request = new CalculateYearPerformanceRequest
        {
            Year = targetYear,
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 100m, ExchangeRate = 1m }
            },
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 110m, ExchangeRate = 1m }
            }
        };

        // Act
        var aggregate = await CalculateAggregateYearPerformanceAsync(request);
        var single = await CalculatePortfolioYearPerformanceAsync(activePortfolio.Id, request);

        // Assert
        aggregate.MissingPrices.Should().BeEmpty();
        single.MissingPrices.Should().BeEmpty();

        // Baseline parity: aggregate home/source valuation fields equal single-portfolio values.
        aggregate.StartValueSource.Should().Be(single.StartValueSource);
        aggregate.EndValueSource.Should().Be(single.EndValueSource);
        aggregate.NetContributionsSource.Should().Be(single.NetContributionsSource);
        aggregate.StartValueHome.Should().Be(single.StartValueHome);
        aggregate.EndValueHome.Should().Be(single.EndValueHome);
        aggregate.NetContributionsHome.Should().Be(single.NetContributionsHome);

        // Parity guard for closed-loop annual return outputs.
        aggregate.ModifiedDietzPercentageSource.Should().Be(single.ModifiedDietzPercentageSource);
        aggregate.ModifiedDietzPercentage.Should().Be(single.ModifiedDietzPercentage);
        aggregate.TimeWeightedReturnPercentageSource.Should().Be(single.TimeWeightedReturnPercentageSource);
        aggregate.TimeWeightedReturnPercentage.Should().Be(single.TimeWeightedReturnPercentage);

        // Regression guard: with mid-year contribution, MD and TWR should not collapse to same value.
        aggregate.ModifiedDietzPercentageSource.Should().NotBeNull();
        aggregate.TimeWeightedReturnPercentageSource.Should().NotBeNull();

        aggregate.ModifiedDietzPercentageSource!.Value
            .Should().BeLessThan(aggregate.TimeWeightedReturnPercentageSource!.Value);
    }

    [Fact]
    public async Task StockTransactions_Response_EnumsAreSerializedAsNumbers()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Enum Serialization Test");
        var transactionRequest = new CreateStockTransactionRequest
        {
            PortfolioId = portfolio.Id,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 10,
            PricePerShare = 150,
            Fees = 0,
            Market = StockMarket.US,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.Margin
        };

        var createResponse = await Client.PostAsJsonAsync("/api/stocktransactions", transactionRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var listResponse = await Client.GetAsync($"/api/stocktransactions?portfolioId={portfolio.Id}");

        // Assert
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await listResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var first = json.RootElement.EnumerateArray().First();

        first.GetProperty("transactionType").ValueKind.Should().Be(JsonValueKind.Number);
        first.GetProperty("market").ValueKind.Should().Be(JsonValueKind.Number);
        first.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task StockTransactions_Create_AcceptsStringEnumsInRequest()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("String Enum Request Test");
        var requestJson = $$"""
{
  "portfolioId": "{{portfolio.Id}}",
  "transactionDate": "{{DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}}",
  "ticker": "TSLA",
  "transactionType": "Buy",
  "shares": 5,
  "fees": 0,
  "pricePerShare": 200,
  "market": "US",
  "currency": "USD",
  "balanceAction": "Margin"
}
""";

        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // Act
        var response = await Client.PostAsync("/api/stocktransactions", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        json.RootElement.GetProperty("transactionType").ValueKind.Should().Be(JsonValueKind.Number);
        json.RootElement.GetProperty("market").ValueKind.Should().Be(JsonValueKind.Number);
        json.RootElement.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Number);
    }
}
