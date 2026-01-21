using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.Benchmark;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供使用者基準標的（User Benchmark）管理 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/user-benchmarks")]
public class UserBenchmarkController(
    GetUserBenchmarksUseCase getUseCase,
    AddUserBenchmarkUseCase addUseCase,
    DeleteUserBenchmarkUseCase deleteUseCase) : ControllerBase
{
    /// <summary>
    /// 取得目前使用者的所有基準標的。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserBenchmarkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserBenchmarkDto>>> GetAll(CancellationToken cancellationToken)
    {
        var benchmarks = await getUseCase.ExecuteAsync(cancellationToken);
        return Ok(benchmarks);
    }

    /// <summary>
    /// 新增基準標的。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserBenchmarkDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserBenchmarkDto>> Create(
        [FromBody] CreateUserBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await addUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetAll), result);
    }

    /// <summary>
    /// 刪除基準標的。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
