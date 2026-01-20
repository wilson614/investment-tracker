using System.Security.Claims;

namespace InvestmentTracker.API.Middleware;

/// <summary>
/// Middleware that ensures authenticated user context is properly set up for tenant isolation.
/// </summary>
public class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                // Store UserId in HttpContext.Items for easy access
                context.Items["UserId"] = userId;

                logger.LogDebug("Tenant context set for user {UserId}", userId);
            }
            else
            {
                logger.LogWarning("Authenticated user has invalid NameIdentifier claim: {Claim}", userIdClaim);
            }
        }

        await next(context);
    }
}

/// <summary>
/// Extension methods for TenantContextMiddleware.
/// </summary>
public static class TenantContextMiddlewareExtensions
{
    public static void UseTenantContext(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<TenantContextMiddleware>();
    }
}
