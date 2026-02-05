using System.Security.Claims;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供使用者註冊、登入與 Token 管理等驗證相關 API。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPortfolioRepository portfolioRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    IAppDbTransactionManager transactionManager,
    IJwtTokenService jwtTokenService) : ControllerBase
{
    /// <summary>
    /// 註冊新使用者帳號。
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 檢查 Email 是否已註冊
        if (await userRepository.ExistsByEmailAsync(normalizedEmail, cancellationToken))
        {
            return Conflict(new { message = "Email already registered" });
        }

        await using var transaction = await transactionManager.BeginTransactionAsync(cancellationToken);

        // 建立使用者
        var passwordHash = jwtTokenService.HashPassword(request.Password);
        var user = new User(normalizedEmail, passwordHash, request.DisplayName.Trim());
        await userRepository.AddAsync(user, cancellationToken);

        // 為新使用者建立預設投資組合（台股 + 美股）
        var twdLedger = new CurrencyLedger(user.Id, "TWD", "TWD Ledger", homeCurrency: "TWD");
        await currencyLedgerRepository.AddAsync(twdLedger, cancellationToken);

        var twdPortfolio = new Portfolio(
            user.Id,
            twdLedger.Id,
            baseCurrency: "TWD",
            homeCurrency: "TWD",
            displayName: "台股投資組合");
        await portfolioRepository.AddAsync(twdPortfolio, cancellationToken);

        var usdLedger = new CurrencyLedger(user.Id, "USD", "USD Ledger", homeCurrency: "TWD");
        await currencyLedgerRepository.AddAsync(usdLedger, cancellationToken);

        var usdPortfolio = new Portfolio(
            user.Id,
            usdLedger.Id,
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "美股投資組合");
        await portfolioRepository.AddAsync(usdPortfolio, cancellationToken);

        // 產生 Token
        var authResponse = await CreateAuthResponseAsync(user, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(Register), authResponse);
    }

    /// <summary>
    /// 使用 Email 與密碼登入。
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null || !jwtTokenService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var authResponse = await CreateAuthResponseAsync(user, cancellationToken);

        return Ok(authResponse);
    }

    /// <summary>
    /// 使用 refresh token 取得新的 access token。
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);

        var storedToken = await refreshTokenRepository.GetByTokenHashWithUserAsync(tokenHash, cancellationToken);

        if (storedToken is not { IsActive: true } || !storedToken.User.IsActive)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        // 撤銷舊 Token
        storedToken.Revoke();

        // 產生新 Token
        var authResponse = await CreateAuthResponseAsync(storedToken.User, cancellationToken, storedToken.Id);

        return Ok(authResponse);
    }

    /// <summary>
    /// 登出並撤銷 refresh token。
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        if (request?.RefreshToken == null)
        {
            return NoContent();
        }

        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);

        var storedToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (storedToken != null)
        {
            storedToken.Revoke();
            await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// 取得目前登入使用者的個人資料。
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

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
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        if (user == null)
            return NotFound();

        // 若有提供 Email，則更新 Email
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            if (normalizedEmail != user.Email)
            {
                // 檢查 Email 是否已被其他使用者使用
                var existingUser = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
                if (existingUser != null && existingUser.Id != userId)
                    return Conflict(new { message = "Email already in use" });

                user.UpdateEmail(normalizedEmail);
            }
        }

        // 若有提供顯示名稱，則更新顯示名稱
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.UpdateDisplayName(request.DisplayName.Trim());
        }

        await userRepository.UpdateAsync(user, cancellationToken);

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
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        if (user == null)
            return NotFound();

        if (!jwtTokenService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Current password is incorrect" });

        var newPasswordHash = jwtTokenService.HashPassword(request.NewPassword);
        user.UpdatePassword(newPasswordHash);

        await userRepository.UpdateAsync(user, cancellationToken);

        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(User user, CancellationToken cancellationToken, Guid? replacedTokenId = null)
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
            var oldToken = await refreshTokenRepository.GetByIdAsync(replacedTokenId.Value, cancellationToken);
            oldToken?.Revoke(refreshToken.Id);
        }

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

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
