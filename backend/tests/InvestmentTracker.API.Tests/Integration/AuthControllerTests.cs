using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentTracker.API.Tests.Integration;

public class AuthControllerTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Refresh_ReturnsNewTokens_AndRevokesOldToken()
    {
        // Arrange
        var oldRefreshTokenPlain = "old-refresh-token";

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

            var tokenHash = jwtTokenService.HashToken(oldRefreshTokenPlain);
            var refreshToken = new RefreshToken(TestUserId, tokenHash, DateTime.UtcNow.AddDays(1));
            context.RefreshTokens.Add(refreshToken);
            await context.SaveChangesAsync();
        }

        // Act
        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = oldRefreshTokenPlain });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshToken.Should().NotBe(oldRefreshTokenPlain);
        auth.User.Id.Should().Be(TestUserId);

        // Verify old refresh token is revoked and new token is stored
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

            var oldHash = jwtTokenService.HashToken(oldRefreshTokenPlain);
            var oldToken = await context.RefreshTokens.SingleAsync(x => x.Token == oldHash);
            oldToken.IsActive.Should().BeFalse();
            oldToken.RevokedAt.Should().NotBeNull();

            var newHash = jwtTokenService.HashToken(auth.RefreshToken);
            var newToken = await context.RefreshTokens.SingleAsync(x => x.Token == newHash);
            newToken.IsActive.Should().BeTrue();
        }

        // Old token cannot be reused
        var reuseResponse = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = oldRefreshTokenPlain });
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReturnsUnauthorized_WhenTokenUnknown()
    {
        // Arrange
        var unknownRefreshToken = "unknown-refresh-token";

        // Act
        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = unknownRefreshToken });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class JwtAccessTokenExpirationOverrideTests
{
    [Fact]
    public void JwtTokenService_ReadsAccessTokenExpirationMinutes_FromEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes");

        try
        {
            Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", "7");

            using var factory = new CustomWebApplicationFactory();
            using var scope = factory.Services.CreateScope();

            var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            jwtTokenService.AccessTokenExpirationMinutes.Should().Be(7);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", previous);
        }
    }
}
