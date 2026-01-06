using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Request to register a new user.
/// </summary>
public record RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public required string Password { get; init; }

    [Required]
    [MaxLength(100)]
    public required string DisplayName { get; init; }
}

/// <summary>
/// Request to login.
/// </summary>
public record LoginRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

/// <summary>
/// Request to refresh tokens.
/// </summary>
public record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

/// <summary>
/// Authentication response with tokens.
/// </summary>
public record AuthResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserDto User { get; init; }
}

/// <summary>
/// User information.
/// </summary>
public record UserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
}
