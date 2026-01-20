using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Integration tests for PortfoliosController.
/// Tests the full HTTP request/response cycle including middleware.
/// </summary>
public class PortfoliosControllerTests : IntegrationTestBase
{
    public PortfoliosControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

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
        var request = new { Description = "My Test Portfolio" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/portfolios", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();
        portfolio!.Description.Should().Be("My Test Portfolio");
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
        portfolio!.Id.Should().Be(created.Id);
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
        updated!.Description.Should().Be("Updated Description");
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
}
