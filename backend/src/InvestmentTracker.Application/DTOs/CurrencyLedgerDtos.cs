using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 外幣帳本（Currency Ledger）的資料傳輸物件。
/// </summary>
public record CurrencyLedgerDto
{
    public Guid Id { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HomeCurrency { get; init; } = "TWD";
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 外幣交易（Currency Transaction）的資料傳輸物件。
/// </summary>
public record CurrencyTransactionDto
{
    public Guid Id { get; init; }
    public Guid CurrencyLedgerId { get; init; }
    public DateTime TransactionDate { get; init; }
    public CurrencyTransactionType TransactionType { get; init; }
    public decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public Guid? RelatedStockTransactionId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 外幣帳本摘要（包含計算後的統計欄位）。
/// </summary>
public record CurrencyLedgerSummaryDto
{
    public CurrencyLedgerDto Ledger { get; init; } = null!;

    /// <summary>目前外幣餘額。</summary>
    public decimal Balance { get; init; }

    /// <summary>依所有換匯交易計算的平均匯率（每 1 單位外幣對應的 TWD）。</summary>
    public decimal AverageExchangeRate { get; init; }

    /// <summary>投入的 TWD 淨額（買入 - 賣出）。</summary>
    public decimal TotalExchanged { get; init; }

    /// <summary>用於買股票的外幣總支出。</summary>
    public decimal TotalSpentOnStocks { get; init; }

    /// <summary>外幣利息收入總額。</summary>
    public decimal TotalInterest { get; init; }

    /// <summary>剩餘外幣餘額的成本基礎（移動平均法）。</summary>
    public decimal TotalCost { get; init; }

    /// <summary>賣出外幣的已實現損益。</summary>
    public decimal RealizedPnl { get; init; }

    public decimal? CurrentExchangeRate { get; init; }
    public decimal? CurrentValueHome { get; init; }
    public decimal? UnrealizedPnlHome { get; init; }
    public decimal? UnrealizedPnlPercentage { get; init; }

    public IReadOnlyList<CurrencyTransactionDto> RecentTransactions { get; init; } = [];
}

/// <summary>
/// 建立外幣帳本的請求 DTO。
/// </summary>
public record CreateCurrencyLedgerRequest
{
    public required string CurrencyCode { get; init; }
    public required string Name { get; init; }
    public string HomeCurrency { get; init; } = "TWD";
}

/// <summary>
/// 更新外幣帳本的請求 DTO。
/// </summary>
public record UpdateCurrencyLedgerRequest
{
    public required string Name { get; init; }
}

/// <summary>
/// 建立外幣交易的請求 DTO。
/// </summary>
public record CreateCurrencyTransactionRequest
{
    public required Guid CurrencyLedgerId { get; init; }
    public required DateTime TransactionDate { get; init; }
    public required CurrencyTransactionType TransactionType { get; init; }
    public required decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public Guid? RelatedStockTransactionId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// 更新外幣交易的請求 DTO。
/// </summary>
public record UpdateCurrencyTransactionRequest
{
    public required DateTime TransactionDate { get; init; }
    public required CurrencyTransactionType TransactionType { get; init; }
    public required decimal ForeignAmount { get; init; }
    public decimal? HomeAmount { get; init; }
    public decimal? ExchangeRate { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// 外幣交易 CSV 匯入請求（Application 層）。
/// </summary>
public record ImportCurrencyTransactionsRequest
{
    public required Guid LedgerId { get; init; }
    public required Stream CsvStream { get; init; }
    public string? FileName { get; init; }
}

/// <summary>
/// 外幣交易 CSV 匯入結果。
/// </summary>
public record CurrencyTransactionCsvImportResultDto
{
    /// <summary>
    /// committed | rejected
    /// </summary>
    public string Status { get; init; } = string.Empty;

    public CurrencyTransactionCsvImportSummaryDto Summary { get; init; } = null!;

    public IReadOnlyList<CurrencyTransactionCsvImportErrorDto> Errors { get; init; } = [];
}

/// <summary>
/// 外幣交易 CSV 匯入摘要。
/// </summary>
public record CurrencyTransactionCsvImportSummaryDto
{
    public int TotalRows { get; init; }
    public int InsertedRows { get; init; }
    public int RejectedRows { get; init; }
    public int? ErrorCount { get; init; }
}

/// <summary>
/// 外幣交易 CSV 匯入逐列診斷資訊。
/// </summary>
public record CurrencyTransactionCsvImportErrorDto
{
    public int RowNumber { get; init; }
    public string FieldName { get; init; } = string.Empty;
    public string? InvalidValue { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string CorrectionGuidance { get; init; } = string.Empty;
}
