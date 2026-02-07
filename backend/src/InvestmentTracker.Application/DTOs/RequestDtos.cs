using System.ComponentModel.DataAnnotations;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 建立投資組合的請求 DTO。
/// </summary>
public record CreatePortfolioRequest
{
    [StringLength(500)]
    public string? Description { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    [Range(0, double.MaxValue)]
    public decimal? InitialBalance { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string HomeCurrency { get; init; } = "TWD";

    /// <summary>
    /// 投資組合顯示名稱（例如："美股投資組合"）。
    /// </summary>
    [StringLength(100)]
    public string? DisplayName { get; init; }
}

/// <summary>
/// 更新投資組合的請求 DTO。
/// </summary>
public record UpdatePortfolioRequest
{
    [StringLength(500)]
    public string? Description { get; init; }
}

/// <summary>
/// 建立股票交易的請求 DTO。
/// </summary>
public record CreateStockTransactionRequest
{
    [Required]
    public Guid PortfolioId { get; init; }

    [Required]
    public DateTime TransactionDate { get; init; }

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Ticker { get; init; } = string.Empty;

    [Required]
    public TransactionType TransactionType { get; init; }

    [Required]
    [Range(0.0001, double.MaxValue)]
    public decimal Shares { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal PricePerShare { get; init; }

    /// <summary>
    /// 兌換成本位幣用的匯率。
    /// 若未提供，交易成本僅會以原始幣別追蹤。
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal? ExchangeRate { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

    public bool AutoDeposit { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    /// <summary>
    /// 交易所屬市場。若未提供，會根據 Ticker 自動推測。
    /// </summary>
    public StockMarket? Market { get; init; }

    /// <summary>
    /// 交易計價幣別。若未提供，會根據 Market 自動推測（TW → TWD，其他 → USD）。
    /// </summary>
    public Currency? Currency { get; init; }
}

/// <summary>
/// 更新股票交易的請求 DTO。
/// </summary>
public record UpdateStockTransactionRequest
{
    [Required]
    public DateTime TransactionDate { get; init; }

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Ticker { get; init; } = string.Empty;

    [Required]
    public TransactionType TransactionType { get; init; }

    [Required]
    [Range(0.0001, double.MaxValue)]
    public decimal Shares { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal PricePerShare { get; init; }

    /// <summary>
    /// 兌換成本位幣用的匯率。
    /// 可選填；若未提供，交易成本僅會以原始幣別追蹤。
    /// </summary>
    [Range(0.000001, double.MaxValue)]
    public decimal? ExchangeRate { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

    public bool AutoDeposit { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    /// <summary>
    /// 交易所屬市場。
    /// </summary>
    public StockMarket? Market { get; init; }

    /// <summary>
    /// 交易計價幣別。若未提供，會根據 Market 自動推測（TW → TWD，其他 → USD）。
    /// </summary>
    public Currency? Currency { get; init; }
}

/// <summary>
/// 以現價計算投資組合績效的請求 DTO。
/// </summary>
public record CalculatePerformanceRequest
{
    public Dictionary<string, CurrentPriceInfo> CurrentPrices { get; init; } = [];
}

/// <summary>
/// 計算投資組合 XIRR 的請求 DTO。
/// </summary>
public record CalculateXirrRequest
{
    public Dictionary<string, CurrentPriceInfo>? CurrentPrices { get; init; }
    public DateTime? AsOfDate { get; init; }
}

/// <summary>
/// 計算單一持倉 XIRR 的請求 DTO。
/// </summary>
public record CalculatePositionXirrRequest
{
    public decimal? CurrentPrice { get; init; }
    public decimal? CurrentExchangeRate { get; init; }
    public DateTime? AsOfDate { get; init; }
}

/// <summary>
/// XIRR 計算結果 DTO。
/// </summary>
public record XirrResultDto
{
    public double? Xirr { get; init; }
    public double? XirrPercentage { get; init; }
    public int CashFlowCount { get; init; }
    public DateTime AsOfDate { get; init; }

    /// <summary>
    /// 最早的交易日期，用於判斷 XIRR 計算期間是否過短。
    /// </summary>
    public DateTime? EarliestTransactionDate { get; init; }

    /// <summary>
    /// 交易日缺少匯率、需要手動補齊的清單。
    /// 若匯率皆齊全則為 null。
    /// </summary>
    public IReadOnlyList<MissingExchangeRateDto>? MissingExchangeRates { get; init; }
}

/// <summary>
/// 交易日缺少匯率的資訊。
/// </summary>
public record MissingExchangeRateDto
{
    public DateTime TransactionDate { get; init; }
    public string Currency { get; init; } = string.Empty;
}

/// <summary>
/// 股票代號的現價資訊。
/// </summary>
public record CurrentPriceInfo
{
    public decimal Price { get; init; }
    public decimal ExchangeRate { get; init; }
}

/// <summary>
/// Create bank account request DTO.
/// </summary>
public record CreateBankAccountRequest
{
    [Required]
    [StringLength(100)]
    public required string BankName { get; init; }

    [Range(0, double.MaxValue)]
    public decimal TotalAssets { get; init; }

    /// <summary>Annual interest rate (percentage).</summary>
    [Range(0, double.MaxValue)]
    public decimal InterestRate { get; init; }

    /// <summary>Preferential cap (0 means no cap).</summary>
    [Range(0, double.MaxValue)]
    public decimal InterestCap { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "TWD";
}

/// <summary>
/// Update bank account request DTO.
/// </summary>
public record UpdateBankAccountRequest
{
    [Required]
    [StringLength(100)]
    public required string BankName { get; init; }

    [Range(0, double.MaxValue)]
    public decimal TotalAssets { get; init; }

    /// <summary>Annual interest rate (percentage).</summary>
    [Range(0, double.MaxValue)]
    public decimal InterestRate { get; init; }

    /// <summary>Preferential cap (0 means no cap).</summary>
    [Range(0, double.MaxValue)]
    public decimal InterestCap { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "TWD";
}

/// <summary>
/// Create fund allocation request DTO.
/// </summary>
public record CreateFundAllocationRequest
{
    public string Purpose { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Note { get; init; }
    public bool? IsDisposable { get; init; }
}

/// <summary>
/// Update fund allocation request DTO.
/// </summary>
public record UpdateFundAllocationRequest
{
    public string? Purpose { get; init; }
    public decimal? Amount { get; init; }
    public string? Note { get; init; }
    public bool? IsDisposable { get; init; }
}
