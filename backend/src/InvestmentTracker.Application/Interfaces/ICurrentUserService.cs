namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for accessing the current authenticated user's context.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the current user's ID, or null if not authenticated.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Gets the current user's email, or null if not authenticated.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Indicates whether the current request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
