using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;

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
