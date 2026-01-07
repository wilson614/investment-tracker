using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

[ApiController]
[Route("api/stock-prices")]
[Authorize]
public class StockPricesController : ControllerBase
{
    private readonly IStockPriceService _stockPriceService;

    public StockPricesController(IStockPriceService stockPriceService)
    {
        _stockPriceService = stockPriceService;
    }

    /// <summary>
    /// Get real-time stock quote
    /// </summary>
    /// <param name="market">Market: 1=TW, 2=US, 3=UK</param>
    /// <param name="symbol">Stock symbol (e.g., 2330, AAPL, VOD)</param>
    [HttpGet]
    [ProducesResponseType(typeof(StockQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockQuoteResponse>> GetQuote(
        [FromQuery] StockMarket market,
        [FromQuery] string symbol,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest("Symbol is required");
        }

        if (!Enum.IsDefined(typeof(StockMarket), market))
        {
            return BadRequest("Invalid market. Valid values: 1 (TW), 2 (US), 3 (UK)");
        }

        var quote = await _stockPriceService.GetQuoteAsync(market, symbol.Trim(), cancellationToken);

        if (quote == null)
        {
            return NotFound($"Quote not found for {market}:{symbol}");
        }

        return Ok(quote);
    }

    /// <summary>
    /// Get available markets
    /// </summary>
    [HttpGet("markets")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public ActionResult GetMarkets()
    {
        var markets = new[]
        {
            new { Value = (int)StockMarket.TW, Name = "TW", Description = "台股" },
            new { Value = (int)StockMarket.US, Name = "US", Description = "美股" },
            new { Value = (int)StockMarket.UK, Name = "UK", Description = "英股" }
        };

        return Ok(markets);
    }
}
