using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.UserPreferences;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供使用者偏好設定（User Preferences）管理 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/user-preferences")]
public class UserPreferencesController(
    GetUserPreferencesUseCase getUseCase,
    UpdateUserPreferencesUseCase updateUseCase) : ControllerBase
{
    /// <summary>
    /// 取得目前使用者的偏好設定。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserPreferencesDto>> Get(CancellationToken cancellationToken)
    {
        var preferences = await getUseCase.ExecuteAsync(cancellationToken);
        return Ok(preferences);
    }

    /// <summary>
    /// 更新使用者的偏好設定（部分更新，只更新有傳入的欄位）。
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserPreferencesDto>> Update(
        [FromBody] UpdateUserPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }
}
