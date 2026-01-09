using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
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
    /// Get real-time stock quote (basic)
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
    /// Get real-time stock quote with exchange rate
    /// </summary>
    /// <param name="market">Market: 1=TW, 2=US, 3=UK</param>
    /// <param name="symbol">Stock symbol (e.g., 2330, AAPL, VOD)</param>
    /// <param name="homeCurrency">Home currency for exchange rate (e.g., TWD)</param>
    [HttpGet("with-rate")]
    [ProducesResponseType(typeof(StockQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockQuoteResponse>> GetQuoteWithExchangeRate(
        [FromQuery] StockMarket market,
        [FromQuery] string symbol,
        [FromQuery] string homeCurrency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest("Symbol is required");
        }

        if (string.IsNullOrWhiteSpace(homeCurrency))
        {
            return BadRequest("Home currency is required");
        }

        if (!Enum.IsDefined(typeof(StockMarket), market))
        {
            return BadRequest("Invalid market. Valid values: 1 (TW), 2 (US), 3 (UK)");
        }

        var quote = await _stockPriceService.GetQuoteWithExchangeRateAsync(
            market, symbol.Trim(), homeCurrency.Trim().ToUpperInvariant(), cancellationToken);

        if (quote == null)
        {
            return NotFound($"Quote not found for {market}:{symbol}");
        }

        return Ok(quote);
    }

    /// <summary>
    /// Get exchange rate between two currencies
    /// </summary>
    /// <param name="from">Source currency (e.g., USD)</param>
    /// <param name="to">Target currency (e.g., TWD)</param>
    [HttpGet("exchange-rate")]
    [ProducesResponseType(typeof(ExchangeRateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExchangeRateResponse>> GetExchangeRate(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return BadRequest("Both 'from' and 'to' currencies are required");
        }

        var rate = await _stockPriceService.GetExchangeRateAsync(
            from.Trim().ToUpperInvariant(),
            to.Trim().ToUpperInvariant(),
            cancellationToken);

        if (rate == null)
        {
            return NotFound($"Exchange rate not found for {from}/{to}");
        }

        return Ok(rate);
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
