using InvestmentTracker.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供 ETF 分類（例如配息型/累積型）相關查詢 API。
/// </summary>
[ApiController]
[Route("api/etf-classification")]
[Authorize]
public class EtfClassificationController(
    EtfClassificationService classificationService,
    ILogger<EtfClassificationController> logger) : ControllerBase
{
    private readonly EtfClassificationService _classificationService = classificationService;
    private readonly ILogger<EtfClassificationController> _logger = logger;

    /// <summary>
    /// 取得指定 ticker 的 ETF 分類結果。
    /// </summary>
    [HttpGet("{ticker}")]
    [ProducesResponseType(typeof(EtfClassificationResult), StatusCodes.Status200OK)]
    public ActionResult<EtfClassificationResult> GetClassification(string ticker)
    {
        var result = _classificationService.ClassifyEtf(ticker);
        return Ok(result);
    }

    /// <summary>
    /// 取得系統已知的 ETF 分類清單。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EtfClassificationResult>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<EtfClassificationResult>> GetAllClassifications()
    {
        var results = _classificationService.GetKnownClassifications();
        return Ok(results);
    }

    /// <summary>
    /// 判斷指定 ticker 在報酬計算時是否需要做股利調整。
    /// </summary>
    [HttpGet("{ticker}/needs-dividend-adjustment")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public ActionResult<bool> NeedsDividendAdjustment(string ticker)
    {
        var result = _classificationService.NeedsDividendAdjustment(ticker);
        return Ok(result);
    }
}
