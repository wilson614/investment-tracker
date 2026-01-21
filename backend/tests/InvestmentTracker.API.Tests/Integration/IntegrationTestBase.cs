using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Base class for integration tests providing common setup and utilities.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;
    protected readonly Guid TestUserId;

    protected IntegrationTestBase(CustomWebApplicationFactory factory)
    {
        Factory = factory;
        TestUserId = Guid.NewGuid();
        Factory.TestUserId = TestUserId;
        Client = Factory.CreateClient();

        // Set up authorization header with a test JWT token
        var token = GenerateTestToken(TestUserId);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Initialize test data
        InitializeTestDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Generates a test JWT token for the specified user ID.
    /// </summary>
    protected static string GenerateTestToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(
            "your-256-bit-secret-key-here-minimum-32-chars"u8.ToArray());
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User")
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

    /// <summary>
    /// Initializes common test data (user).
    /// </summary>
    private async Task InitializeTestDataAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check if user already exists
        var existingUser = await context.Users.FindAsync(TestUserId);
        if (existingUser == null)
        {
            var user = new User("test@example.com", "password", "Test User");
            typeof(User).GetProperty("Id")!.SetValue(user, TestUserId);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Creates a portfolio for testing.
    /// </summary>
    protected async Task<PortfolioDto> CreateTestPortfolioAsync(string description = "Test Portfolio")
    {
        var request = new { Description = description };
        var response = await Client.PostAsJsonAsync("/api/portfolios", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioDto>())!;
    }

    /// <summary>
    /// Gets a service from the factory's service provider.
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
