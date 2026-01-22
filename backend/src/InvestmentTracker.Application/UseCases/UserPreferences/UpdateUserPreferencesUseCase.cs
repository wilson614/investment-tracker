using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.UserPreferences;

/// <summary>
/// 更新使用者偏好設定
/// </summary>
public class UpdateUserPreferencesUseCase(
    IUserPreferencesRepository preferencesRepository,
    ICurrentUserService currentUserService)
{
    public async Task<UserPreferencesDto> ExecuteAsync(
        UpdateUserPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User is not authenticated");

        var preferences = await preferencesRepository.GetByUserIdAsync(userId, cancellationToken);

        if (preferences == null)
        {
            // 建立新的偏好設定
            preferences = new Domain.Entities.UserPreferences(userId);

            if (request.YtdBenchmarkPreferences != null)
                preferences.SetYtdBenchmarkPreferences(request.YtdBenchmarkPreferences);
            if (request.CapeRegionPreferences != null)
                preferences.SetCapeRegionPreferences(request.CapeRegionPreferences);
            if (request.DefaultPortfolioId != null)
                preferences.SetDefaultPortfolioId(request.DefaultPortfolioId);

            await preferencesRepository.AddAsync(preferences, cancellationToken);
        }
        else
        {
            // 更新現有偏好設定（只更新有傳入的欄位）
            if (request.YtdBenchmarkPreferences != null)
                preferences.SetYtdBenchmarkPreferences(request.YtdBenchmarkPreferences);
            if (request.CapeRegionPreferences != null)
                preferences.SetCapeRegionPreferences(request.CapeRegionPreferences);
            if (request.DefaultPortfolioId != null)
                preferences.SetDefaultPortfolioId(request.DefaultPortfolioId);

            await preferencesRepository.UpdateAsync(preferences, cancellationToken);
        }

        return new UserPreferencesDto
        {
            YtdBenchmarkPreferences = preferences.YtdBenchmarkPreferences,
            CapeRegionPreferences = preferences.CapeRegionPreferences,
            DefaultPortfolioId = preferences.DefaultPortfolioId
        };
    }
}
