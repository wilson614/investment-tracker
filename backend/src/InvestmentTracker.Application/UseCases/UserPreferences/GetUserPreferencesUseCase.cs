using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.UserPreferences;

/// <summary>
/// 取得使用者偏好設定
/// </summary>
public class GetUserPreferencesUseCase(
    IUserPreferencesRepository preferencesRepository,
    ICurrentUserService currentUserService)
{
    public async Task<UserPreferencesDto> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User is not authenticated");

        var preferences = await preferencesRepository.GetByUserIdAsync(userId, cancellationToken);

        if (preferences == null)
        {
            // 回傳預設值
            return new UserPreferencesDto
            {
                YtdBenchmarkPreferences = null,
                CapeRegionPreferences = null,
                DefaultPortfolioId = null
            };
        }

        return new UserPreferencesDto
        {
            YtdBenchmarkPreferences = preferences.YtdBenchmarkPreferences,
            CapeRegionPreferences = preferences.CapeRegionPreferences,
            DefaultPortfolioId = preferences.DefaultPortfolioId
        };
    }
}
