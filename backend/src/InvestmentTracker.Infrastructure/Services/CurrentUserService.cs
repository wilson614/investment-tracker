using System.Security.Claims;
using InvestmentTracker.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 從 HttpContext 取得目前已驗證使用者資訊的服務。
/// </summary>
public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }
    }

    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
