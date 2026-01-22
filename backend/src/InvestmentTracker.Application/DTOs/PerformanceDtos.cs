namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 歷史年度績效計算的回應 DTO。
/// </summary>
public record YearPerformanceDto
{
    /// <summary>此筆績效計算對應的年份。</summary>
    public int Year { get; init; }

    // ===== 本位幣（Home Currency，例如 TWD）績效 =====

    /// <summary>本位幣 XIRR（年化報酬率）。</summary>
    public double? Xirr { get; init; }

    /// <summary>本位幣 XIRR 百分比（例如 12.5 代表 12.5%）。</summary>
    public double? XirrPercentage { get; init; }

    /// <summary>本位幣年度總報酬率百分比。</summary>
    public double? TotalReturnPercentage { get; init; }

    /// <summary>年度起始資產價值（本位幣）。</summary>
    public decimal? StartValueHome { get; init; }

    /// <summary>年度結束資產價值（本位幣）。</summary>
    public decimal? EndValueHome { get; init; }

    /// <summary>年度期間淨投入（本位幣）。</summary>
    public decimal NetContributionsHome { get; init; }

    // ===== 原始幣別（Source/Base Currency，例如 USD）績效 =====

    /// <summary>投資組合的原始/基準幣別（例如 USD）。</summary>
    public string? SourceCurrency { get; init; }

    /// <summary>原始幣別 XIRR（年化報酬率）。</summary>
    public double? XirrSource { get; init; }

    /// <summary>原始幣別 XIRR 百分比（例如 12.5 代表 12.5%）。</summary>
    public double? XirrPercentageSource { get; init; }

    /// <summary>原始幣別年度總報酬率百分比。</summary>
    public double? TotalReturnPercentageSource { get; init; }

    /// <summary>年度起始資產價值（原始幣別）。</summary>
    public decimal? StartValueSource { get; init; }

    /// <summary>年度結束資產價值（原始幣別）。</summary>
    public decimal? EndValueSource { get; init; }

    /// <summary>年度期間淨投入（原始幣別）。</summary>
    public decimal? NetContributionsSource { get; init; }

    // ===== 共通欄位 =====

    /// <summary>XIRR 計算所使用的現金流筆數。</summary>
    public int CashFlowCount { get; init; }

    /// <summary>年度期間實際交易筆數（僅 buy/sell，不含年度起訖估值）。</summary>
    public int TransactionCount { get; init; }

    /// <summary>
    /// 年度期間最早的交易日期。用於判斷 XIRR 計算期間是否過短（< 3 個月）。
    /// </summary>
    public DateTime? EarliestTransactionDateInYear { get; init; }

    /// <summary>計算所需但缺少參考價格的持倉清單。</summary>
    public IReadOnlyList<MissingPriceDto> MissingPrices { get; init; } = [];

    /// <summary>是否可以完整計算績效（沒有缺少價格）。</summary>
    public bool IsComplete => MissingPrices.Count == 0;
}

/// <summary>
/// 歷史計算中缺少價格的持倉資訊。
/// </summary>
public record MissingPriceDto
{
    /// <summary>股票代號。</summary>
    public string Ticker { get; init; } = string.Empty;

    /// <summary>需要價格的日期。</summary>
    public DateTime Date { get; init; }

    /// <summary>價格類型：年初參考價或年末/截止日參考價。</summary>
    public string PriceType { get; init; } = "YearEnd";

    /// <summary>股票市場（用於正確轉換 Yahoo Finance 符號）。</summary>
    public int? Market { get; init; }
}

/// <summary>
/// 可用績效年份清單的回應 DTO。
/// </summary>
public record AvailableYearsDto
{
    /// <summary>有交易資料的年份列表。</summary>
    public IReadOnlyList<int> Years { get; init; } = [];

    /// <summary>最早有交易的年份。</summary>
    public int? EarliestYear { get; init; }

    /// <summary>目前年份。</summary>
    public int CurrentYear { get; init; } = DateTime.UtcNow.Year;
}

/// <summary>
/// 計算年度績效的請求 DTO。
/// </summary>
public record CalculateYearPerformanceRequest
{
    /// <summary>要計算績效的年份。</summary>
    public int Year { get; init; }

    /// <summary>年度「年末」參考價格（以 ticker 為 key）。</summary>
    public Dictionary<string, YearEndPriceInfo>? YearEndPrices { get; init; }

    /// <summary>年度「年初」參考價格（以 ticker 為 key）。若未提供，會回退使用 YearEndPrices。</summary>
    public Dictionary<string, YearEndPriceInfo>? YearStartPrices { get; init; }
}

/// <summary>
/// 年末參考價格資訊。
/// </summary>
public record YearEndPriceInfo
{
    /// <summary>指定參考日期的原始幣別價格。</summary>
    public decimal Price { get; init; }

    /// <summary>參考日期的匯率（原始幣別轉本位幣）。</summary>
    public decimal ExchangeRate { get; init; }
}
