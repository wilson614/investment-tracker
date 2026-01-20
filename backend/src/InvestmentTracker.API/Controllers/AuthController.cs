using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供使用者註冊、登入與 Token 管理等驗證相關 API。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext context, IJwtTokenService jwtTokenService) : ControllerBase
{
    /// <summary>
    /// 註冊新使用者帳號。
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 檢查 Email 是否已註冊
        var existingUser = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (existingUser != null)
        {
            return Conflict(new { message = "Email already registered" });
        }

        // 建立使用者
        var passwordHash = jwtTokenService.HashPassword(request.Password);
        var user = new User(normalizedEmail, passwordHash, request.DisplayName.Trim());

        context.Users.Add(user);

        // 為新使用者建立預設投資組合
        var portfolio = new Portfolio(user.Id);
        context.Portfolios.Add(portfolio);

        await context.SaveChangesAsync();

        // 產生 Token
        var authResponse = await CreateAuthResponse(user);

        return CreatedAtAction(nameof(Register), authResponse);
    }

    /// <summary>
    /// 使用 Email 與密碼登入。
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive);

        if (user == null || !jwtTokenService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var authResponse = await CreateAuthResponse(user);

        return Ok(authResponse);
    }

    /// <summary>
    /// 使用 refresh token 取得新的 access token。
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);

        var storedToken = await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (storedToken == null || !storedToken.IsActive || !storedToken.User.IsActive)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        // 撤銷舊 Token
        storedToken.Revoke();

        // 產生新 Token
        var authResponse = await CreateAuthResponse(storedToken.User, storedToken.Id);

        return Ok(authResponse);
    }

    /// <summary>
    /// 登出並撤銷 refresh token。
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
    {
        if (request?.RefreshToken == null)
        {
            return NoContent();
        }

        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);

        var storedToken = await context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (storedToken != null)
        {
            storedToken.Revoke();
            await context.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>
    /// 取得目前登入使用者的個人資料。
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetCurrentUserId();
        var user = await context.Users.FindAsync(userId);

        if (user == null)
            return NotFound();

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        });
    }

    /// <summary>
    /// 更新目前登入使用者的個人資料。
    /// </summary>
    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileRequest request)
    {
        var userId = GetCurrentUserId();
        var user = await context.Users.FindAsync(userId);

        if (user == null)
            return NotFound();

        // 若有提供 Email，則更新 Email
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            if (normalizedEmail != user.Email)
            {
                var existingUser = await context.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Id != userId);

                if (existingUser != null)
                    return Conflict(new { message = "Email already in use" });

                user.UpdateEmail(normalizedEmail);
            }
        }

        // 若有提供顯示名稱，則更新顯示名稱
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.UpdateDisplayName(request.DisplayName.Trim());
        }

        await context.SaveChangesAsync();

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        });
    }

    /// <summary>
    /// 變更目前登入使用者的密碼。
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId();
        var user = await context.Users.FindAsync(userId);

        if (user == null)
            return NotFound();

        if (!jwtTokenService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Current password is incorrect" });

        var newPasswordHash = jwtTokenService.HashPassword(request.NewPassword);
        user.UpdatePassword(newPasswordHash);

        await context.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    private async Task<AuthResponse> CreateAuthResponse(User user, Guid? replacedTokenId = null)
    {
        var accessToken = jwtTokenService.GenerateAccessToken(user);
        var refreshTokenValue = jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = jwtTokenService.HashToken(refreshTokenValue);

        var refreshToken = new RefreshToken(
            user.Id,
            refreshTokenHash,
            DateTime.UtcNow.AddDays(jwtTokenService.RefreshTokenExpirationDays));

        if (replacedTokenId.HasValue)
        {
            var oldToken = await context.RefreshTokens.FindAsync(replacedTokenId.Value);
            oldToken?.Revoke(refreshToken.Id);
        }

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddMinutes(jwtTokenService.AccessTokenExpirationMinutes),
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName
            }
        };
    }
}
