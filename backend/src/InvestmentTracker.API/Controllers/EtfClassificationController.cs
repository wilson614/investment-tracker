using InvestmentTracker.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Controller for ETF classification endpoints.
/// </summary>
[ApiController]
[Route("api/etf-classification")]
[Authorize]
public class EtfClassificationController : ControllerBase
{
    private readonly EtfClassificationService _classificationService;
    private readonly ILogger<EtfClassificationController> _logger;

    public EtfClassificationController(
        EtfClassificationService classificationService,
        ILogger<EtfClassificationController> logger)
    {
        _classificationService = classificationService;
        _logger = logger;
    }

    /// <summary>
    /// Get classification for a specific ticker.
    /// </summary>
    [HttpGet("{ticker}")]
    [ProducesResponseType(typeof(EtfClassificationResult), StatusCodes.Status200OK)]
    public ActionResult<EtfClassificationResult> GetClassification(string ticker)
    {
        var result = _classificationService.ClassifyEtf(ticker);
        return Ok(result);
    }

    /// <summary>
    /// Get all known ETF classifications.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EtfClassificationResult>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<EtfClassificationResult>> GetAllClassifications()
    {
        var results = _classificationService.GetKnownClassifications();
        return Ok(results);
    }

    /// <summary>
    /// Check if dividend adjustment is needed for a ticker.
    /// </summary>
    [HttpGet("{ticker}/needs-dividend-adjustment")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public ActionResult<bool> NeedsDividendAdjustment(string ticker)
    {
        var result = _classificationService.NeedsDividendAdjustment(ticker);
        return Ok(result);
    }
}
