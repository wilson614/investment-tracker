using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 取得股票價格的請求 DTO。
/// </summary>
public record StockPriceRequest
{
    public StockMarket Market { get; init; }
    public string Symbol { get; init; } = string.Empty;
}

/// <summary>
/// 股票報價的回應 DTO。
/// </summary>
public record StockQuoteResponse
{
    public string Symbol { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? Change { get; init; }
    public string? ChangePercent { get; init; }
    public StockMarket Market { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; }

    /// <summary>換算成本位幣的匯率（例如 USD/TWD）。</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>匯率幣別對描述（例如 "USD/TWD"）。</summary>
    public string? ExchangeRatePair { get; init; }
}

/// <summary>
/// 匯率查詢的回應 DTO。
/// </summary>
public record ExchangeRateResponse
{
    public string FromCurrency { get; init; } = string.Empty;
    public string ToCurrency { get; init; } = string.Empty;
    public decimal Rate { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; }
}

/// <summary>
/// 單一基準 ETF 的年初至今（YTD）報酬。
/// </summary>
public record MarketYtdReturnDto
{
    /// <summary>市場分類 key（例如 "All Country"、"US Large"、"Taiwan"、"Emerging Markets"）。</summary>
    public string MarketKey { get; init; } = string.Empty;

    /// <summary>ETF 股票代號（例如 "VWRA"、"VUAA"、"0050"、"VFEM"）。</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>可讀性較佳的名稱。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>YTD 基準參考價（通常為前一年 12 月收盤價）。</summary>
    public decimal? Jan1Price { get; init; }

    /// <summary>目前價格。</summary>
    public decimal? CurrentPrice { get; init; }

    /// <summary>YTD 已配息總額（用於含息報酬）。</summary>
    public decimal? DividendsPaid { get; init; }

    /// <summary>YTD 報酬率百分比（僅價格，不含股息）。</summary>
    public decimal? YtdReturnPercent { get; init; }

    /// <summary>YTD 總報酬率百分比（含股息）。</summary>
    public decimal? YtdTotalReturnPercent { get; init; }

    /// <summary>目前價格的抓取時間。</summary>
    public DateTime? FetchedAt { get; init; }

    /// <summary>抓價失敗時的錯誤訊息。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 所有基準 ETF 的 YTD 報酬比較結果。
/// </summary>
public record MarketYtdComparisonDto
{
    /// <summary>YTD 計算年度。</summary>
    public int Year { get; init; }

    /// <summary>基準 ETF 的 YTD 報酬清單。</summary>
    public List<MarketYtdReturnDto> Benchmarks { get; init; } = [];

    /// <summary>資料產生時間。</summary>
    public DateTime GeneratedAt { get; init; }
}
