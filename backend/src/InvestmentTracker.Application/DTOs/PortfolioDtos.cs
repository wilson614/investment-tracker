using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 投資組合摘要的資料傳輸物件。
/// </summary>
public record PortfolioDto
{
    public Guid Id { get; init; }
    public string? Description { get; init; }
    public string BaseCurrency { get; init; } = "USD";
    public string HomeCurrency { get; init; } = "TWD";
    public bool IsActive { get; init; }
    public PortfolioType PortfolioType { get; init; } = PortfolioType.Primary;
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 股票交易的資料傳輸物件。
/// </summary>
public record StockTransactionDto
{
    public Guid Id { get; init; }
    public Guid PortfolioId { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public TransactionType TransactionType { get; init; }
    public decimal Shares { get; init; }
    public decimal PricePerShare { get; init; }
    public decimal? ExchangeRate { get; init; }
    public decimal Fees { get; init; }
    public FundSource FundSource { get; init; }
    public Guid? CurrencyLedgerId { get; init; }
    public string? Notes { get; init; }
    public decimal TotalCostSource { get; init; }
    public decimal? TotalCostHome { get; init; }

    /// <summary>是否包含可用於換算本位幣的匯率。</summary>
    public bool HasExchangeRate { get; init; }

    public decimal? RealizedPnlHome { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // 拆股調整欄位（FR-052a：顯示原始與調整後的數值）

    /// <summary>套用拆股比例後的股數（= Shares × SplitRatio）。</summary>
    public decimal? AdjustedShares { get; init; }

    /// <summary>套用拆股比例後的單價（= PricePerShare / SplitRatio）。</summary>
    public decimal? AdjustedPricePerShare { get; init; }

    /// <summary>此筆交易套用的累計拆股比例（若無拆股則為 1.0）。</summary>
    public decimal SplitRatio { get; init; } = 1.0m;

    /// <summary>此筆交易是否已套用拆股調整。</summary>
    public bool HasSplitAdjustment { get; init; }

    /// <summary>交易所屬市場。</summary>
    public StockMarket Market { get; init; }
}

/// <summary>
/// 依交易紀錄計算出的股票持倉資料。
/// </summary>
public record StockPositionDto
{
    public string Ticker { get; init; } = string.Empty;
    public decimal TotalShares { get; init; }
    public decimal? TotalCostHome { get; init; }
    public decimal TotalCostSource { get; init; }
    public decimal? AverageCostPerShareHome { get; init; }
    public decimal AverageCostPerShareSource { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? CurrentExchangeRate { get; init; }
    public decimal? CurrentValueHome { get; init; }
    public decimal? UnrealizedPnlHome { get; init; }
    public decimal? UnrealizedPnlPercentage { get; init; }
}

/// <summary>
/// 投資組合摘要（含持倉清單）的資料傳輸物件。
/// </summary>
public record PortfolioSummaryDto
{
    public PortfolioDto Portfolio { get; init; } = null!;
    public IReadOnlyList<StockPositionDto> Positions { get; init; } = [];
    public decimal TotalCostHome { get; init; }
    public decimal? TotalValueHome { get; init; }
    public decimal? TotalUnrealizedPnlHome { get; init; }
    public decimal? TotalUnrealizedPnlPercentage { get; init; }
}
