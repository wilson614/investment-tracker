using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供股價/匯率即時報價相關 API。
/// </summary>
[ApiController]
[Route("api/stock-prices")]
[Authorize]
public class StockPricesController(IStockPriceService stockPriceService) : ControllerBase
{
    /// <summary>
    /// 取得即時股價報價（基本資訊）。
    /// </summary>
    /// <param name="market">市場：1=TW、2=US、3=UK</param>
    /// <param name="symbol">股票代號（例如：2330、AAPL、VOD）</param>
    /// <param name="cancellationToken"></param>
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

        var quote = await stockPriceService.GetQuoteAsync(market, symbol.Trim(), cancellationToken);

        if (quote == null)
        {
            return NotFound($"Quote not found for {market}:{symbol}");
        }

        return Ok(quote);
    }

    /// <summary>
    /// 取得即時股價報價（包含匯率）。
    /// </summary>
    /// <param name="market">市場：1=TW、2=US、3=UK</param>
    /// <param name="symbol">股票代號（例如：2330、AAPL、VOD）</param>
    /// <param name="homeCurrency">換算匯率使用的本位幣（例如：TWD）</param>
    /// <param name="cancellationToken"></param>
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

        var quote = await stockPriceService.GetQuoteWithExchangeRateAsync(
            market, symbol.Trim(), homeCurrency.Trim().ToUpperInvariant(), cancellationToken);

        if (quote == null)
        {
            return NotFound($"Quote not found for {market}:{symbol}");
        }

        return Ok(quote);
    }

    /// <summary>
    /// 取得兩種幣別間的即時匯率。
    /// </summary>
    /// <param name="from">來源幣別（例如：USD）</param>
    /// <param name="to">目標幣別（例如：TWD）</param>
    /// <param name="cancellationToken"></param>
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

        var rate = await stockPriceService.GetExchangeRateAsync(
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
    /// 取得可用市場清單。
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
