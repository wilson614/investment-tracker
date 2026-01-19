namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// 取得目前已驗證使用者資訊的服務介面。
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// 取得目前使用者 ID；若未登入則為 null。
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// 取得目前使用者 Email；若未登入則為 null。
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// 目前請求是否已驗證。
    /// </summary>
    bool IsAuthenticated { get; }
}
