using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Preview 產生後、Execute 驗證使用的 session snapshot。
/// </summary>
public record StockImportSessionSnapshotDto
{
    public Guid SessionId { get; init; }
    public Guid UserId { get; init; }
    public Guid PortfolioId { get; init; }
    public string SelectedFormat { get; init; } = string.Empty;
    public string DetectedFormat { get; init; } = string.Empty;
    public IReadOnlyList<StockImportSessionRowSnapshotDto> Rows { get; init; } = [];
}

/// <summary>
/// Preview row 的可驗證快照。
/// </summary>
public record StockImportSessionRowSnapshotDto
{
    public int RowNumber { get; init; }
    public DateTime? TradeDate { get; init; }
    public string? Ticker { get; init; }
    public string TradeSide { get; init; } = string.Empty;
    public string? ConfirmedTradeSide { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal Fees { get; init; }
    public decimal Taxes { get; init; }
    public decimal? NetSettlement { get; init; }
    public string? Currency { get; init; }
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> ActionsRequired { get; init; } = [];
    public bool IsInvalid { get; init; }
}

/// <summary>
/// 股票匯入預覽回應 DTO。
/// </summary>
public record StockImportPreviewResponseDto
{
    public Guid SessionId { get; init; }

    /// <summary>
    /// legacy_csv | broker_statement | unknown
    /// </summary>
    public string DetectedFormat { get; init; } = string.Empty;

    /// <summary>
    /// legacy_csv | broker_statement
    /// </summary>
    public string SelectedFormat { get; init; } = string.Empty;

    public StockImportPreviewSummaryDto Summary { get; init; } = null!;

    public IReadOnlyList<StockImportPreviewRowDto> Rows { get; init; } = [];

    public IReadOnlyList<StockImportDiagnosticDto> Errors { get; init; } = [];
}

/// <summary>
/// 股票匯入預覽摘要。
/// </summary>
public record StockImportPreviewSummaryDto
{
    public int TotalRows { get; init; }
    public int ValidRows { get; init; }
    public int RequiresActionRows { get; init; }
    public int InvalidRows { get; init; }
}

/// <summary>
/// 股票匯入預覽逐列資訊。
/// </summary>
public record StockImportPreviewRowDto
{
    public int RowNumber { get; init; }
    public DateTime? TradeDate { get; init; }
    public string? RawSecurityName { get; init; }
    public string? Ticker { get; init; }

    /// <summary>
    /// buy | sell | ambiguous
    /// </summary>
    public string TradeSide { get; init; } = string.Empty;

    /// <summary>
    /// Canonical confirmed trade side after user confirmation: buy | sell.
    /// </summary>
    public string? ConfirmedTradeSide { get; init; }

    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal Fees { get; init; }
    public decimal Taxes { get; init; }
    public decimal? NetSettlement { get; init; }
    public string? Currency { get; init; }

    /// <summary>
    /// valid | requires_user_action | invalid
    /// </summary>
    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<string> ActionsRequired { get; init; } = [];

    /// <summary>
    /// 若為餘額不足列，回傳決策上下文供前端顯示與帶入執行參數。
    /// </summary>
    public StockImportBalanceDecisionContextDto? BalanceDecision { get; init; }
}

/// <summary>
/// 股票匯入執行回應 DTO。
/// </summary>
public record StockImportExecuteResponseDto
{
    /// <summary>
    /// committed | partially_committed | rejected
    /// </summary>
    public string Status { get; init; } = string.Empty;

    public StockImportExecuteSummaryDto Summary { get; init; } = null!;

    public IReadOnlyList<StockImportExecuteRowResultDto> Results { get; init; } = [];

    public IReadOnlyList<StockImportDiagnosticDto> Errors { get; init; } = [];
}

/// <summary>
/// 股票匯入執行摘要。
/// </summary>
public record StockImportExecuteSummaryDto
{
    public int TotalRows { get; init; }
    public int InsertedRows { get; init; }
    public int FailedRows { get; init; }
    public int ErrorCount { get; init; }
}

/// <summary>
/// 股票匯入執行逐列結果。
/// </summary>
public record StockImportExecuteRowResultDto
{
    public int RowNumber { get; init; }
    public bool Success { get; init; }
    public Guid? TransactionId { get; init; }
    public string? ErrorCode { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Canonical confirmed trade side used during execution: buy | sell.
    /// </summary>
    public string? ConfirmedTradeSide { get; init; }

    public StockImportBalanceDecisionContextDto? BalanceDecision { get; init; }
}

/// <summary>
/// 股票匯入共用逐列診斷資訊（preview / execute 共用）。
/// </summary>
public record StockImportDiagnosticDto
{
    public int RowNumber { get; init; }
    public string FieldName { get; init; } = string.Empty;
    public string? InvalidValue { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string CorrectionGuidance { get; init; } = string.Empty;
}

/// <summary>
/// 餘額不足決策上下文（預覽提示 + 執行回顯）。
/// </summary>
public record StockImportBalanceDecisionContextDto
{
    public decimal RequiredAmount { get; init; }
    public decimal AvailableBalance { get; init; }
    public decimal Shortfall { get; init; }

    /// <summary>
    /// global_default | row_override
    /// </summary>
    public string? DecisionScope { get; init; }

    public BalanceAction? Action { get; init; }
    public CurrencyTransactionType? TopUpTransactionType { get; init; }
}
