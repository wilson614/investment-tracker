using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 註冊新使用者的請求。
/// </summary>
public record RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    [MinLength(6)]
    [MaxLength(128)]
    public required string Password { get; init; }

    [Required]
    [MaxLength(100)]
    public required string DisplayName { get; init; }
}

/// <summary>
/// 使用者登入的請求。
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
/// 重新整理 token 的請求。
/// </summary>
public record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

/// <summary>
/// 驗證成功後回傳的 token 與使用者資訊。
/// </summary>
public record AuthResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserDto User { get; init; }
}

/// <summary>
/// 使用者基本資訊。
/// </summary>
public record UserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// 更新使用者個人資料的請求。
/// </summary>
public record UpdateUserProfileRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; init; }

    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }
}

/// <summary>
/// 變更密碼的請求。
/// </summary>
public record ChangePasswordRequest
{
    [Required]
    public required string CurrentPassword { get; init; }

    [Required]
    [MinLength(6)]
    [MaxLength(128)]
    public required string NewPassword { get; init; }
}
