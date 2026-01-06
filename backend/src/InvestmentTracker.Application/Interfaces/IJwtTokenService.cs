using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for generating and validating JWT tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates an access token for the specified user.
    /// </summary>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Hashes a refresh token for secure storage.
    /// </summary>
    string HashToken(string token);

    /// <summary>
    /// Validates a password against a stored hash.
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);

    /// <summary>
    /// Hashes a password for secure storage.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Gets the access token expiration time in minutes.
    /// </summary>
    int AccessTokenExpirationMinutes { get; }

    /// <summary>
    /// Gets the refresh token expiration time in days.
    /// </summary>
    int RefreshTokenExpirationDays { get; }
}
