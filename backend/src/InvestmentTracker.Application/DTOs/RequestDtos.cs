using System.Collections.Generic;
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

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

    /// <summary>How to handle insufficient ledger balance: None (reject), Margin (allow negative), TopUp (create currency tx)</summary>
    public BalanceAction BalanceAction { get; init; } = BalanceAction.None;

    /// <summary>Required when BalanceAction=TopUp. Must be one of Deposit, InitialBalance, Interest, OtherIncome</summary>
    public CurrencyTransactionType? TopUpTransactionType { get; init; }

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

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

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
/// 預覽股票匯入（支援 legacy CSV 與 broker statement）的請求 DTO。
/// </summary>
public record PreviewStockImportRequest
{
    [Required]
    public Guid PortfolioId { get; init; }

    [Required]
    public required string CsvContent { get; init; }

    [Required]
    [RegularExpression("^(legacy_csv|broker_statement)$", ErrorMessage = "SelectedFormat must be either 'legacy_csv' or 'broker_statement'.")]
    public required string SelectedFormat { get; init; }
}

/// <summary>
/// 執行股票匯入的請求 DTO。
/// </summary>
public record ExecuteStockImportRequest : IValidatableObject
{
    [Required]
    public Guid SessionId { get; init; }

    [Required]
    public Guid PortfolioId { get; init; }

    [Required]
    [MinLength(1, ErrorMessage = "Rows must contain at least one item.")]
    public IReadOnlyList<ExecuteStockImportRowRequest> Rows { get; init; } = [];

    /// <summary>
    /// 預設餘額處理決策（套用到餘額不足列，可被逐列覆寫）。
    /// </summary>
    public StockImportDefaultBalanceDecisionRequest? DefaultBalanceAction { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rows.Count == 0)
        {
            yield return new ValidationResult(
                "Rows must contain at least one item.",
                [nameof(Rows)]);
        }

        if (DefaultBalanceAction is not null)
        {
            if (!DefaultBalanceAction.Action.HasValue)
            {
                yield return new ValidationResult(
                    "DefaultBalanceAction.Action is required when DefaultBalanceAction is provided.",
                    [nameof(DefaultBalanceAction)]);
            }
            else
            {
                if (!StockBalanceActionRules.IsExecutableDefaultAction(DefaultBalanceAction.Action))
                {
                    yield return new ValidationResult(
                        "DefaultBalanceAction.Action must be None, Margin, or TopUp when DefaultBalanceAction is provided.",
                        [nameof(DefaultBalanceAction)]);
                }

                if (DefaultBalanceAction.Action == BalanceAction.TopUp)
                {
                    if (DefaultBalanceAction.TopUpTransactionType.HasValue
                        && !StockBalanceActionRules.IsImportTopUpIncomeType(DefaultBalanceAction.TopUpTransactionType.Value))
                    {
                        yield return new ValidationResult(
                            "DefaultBalanceAction.TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when DefaultBalanceAction.Action is TopUp.",
                            [nameof(DefaultBalanceAction)]);
                    }
                }
                else if (DefaultBalanceAction.TopUpTransactionType.HasValue)
                {
                    yield return new ValidationResult(
                        "DefaultBalanceAction.TopUpTransactionType is only allowed when DefaultBalanceAction.Action is TopUp.",
                        [nameof(DefaultBalanceAction)]);
                }
            }
        }

        var duplicatedRowNumbers = Rows
            .GroupBy(row => row.RowNumber)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(rowNumber => rowNumber)
            .ToList();

        if (duplicatedRowNumbers.Count > 0)
        {
            yield return new ValidationResult(
                $"Rows.RowNumber contains duplicates: {string.Join(",", duplicatedRowNumbers)}.",
                [nameof(Rows)]);
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var rowMember = $"{nameof(Rows)}[{i}]";

            var resolvedAction = row.BalanceAction
                ?? DefaultBalanceAction?.Action
                ?? BalanceAction.None;

            var resolvedTopUpTransactionType = resolvedAction == BalanceAction.TopUp
                ? row.TopUpTransactionType ?? DefaultBalanceAction?.TopUpTransactionType
                : null;

            if (resolvedAction != BalanceAction.TopUp && row.TopUpTransactionType.HasValue)
            {
                yield return new ValidationResult(
                    $"Rows[{i}].TopUpTransactionType is only allowed when resolved BalanceAction is TopUp.",
                    [rowMember]);
            }

            if (resolvedAction == BalanceAction.TopUp
                && resolvedTopUpTransactionType.HasValue
                && !StockBalanceActionRules.IsImportTopUpIncomeType(resolvedTopUpTransactionType.Value))
            {
                yield return new ValidationResult(
                    $"Rows[{i}].TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when resolved BalanceAction is TopUp.",
                    [rowMember]);
            }
        }
    }
}

/// <summary>
/// 股票匯入逐列執行參數。
/// </summary>
public record ExecuteStockImportRowRequest
{
    [Range(1, int.MaxValue)]
    public int RowNumber { get; init; }

    [StringLength(20, MinimumLength = 1)]
    public string? Ticker { get; init; }

    /// <summary>
    /// Canonical confirmed trade side: buy | sell.
    /// 當預覽列為 ambiguous 時，執行前需提供此欄位。
    /// </summary>
    [RegularExpression("^(buy|sell)$", ErrorMessage = "ConfirmedTradeSide must be either 'buy' or 'sell'.")]
    public string? ConfirmedTradeSide { get; init; }

    public bool Exclude { get; init; }

    /// <summary>
    /// 逐列餘額不足決策（None / Margin / TopUp）。
    /// </summary>
    public BalanceAction? BalanceAction { get; init; }

    /// <summary>
    /// 僅當 BalanceAction=TopUp 時需要。
    /// </summary>
    public CurrencyTransactionType? TopUpTransactionType { get; init; }
}

/// <summary>
/// 股票匯入預設餘額決策（global default）。
/// </summary>
public record StockImportDefaultBalanceDecisionRequest
{
    /// <summary>
    /// 建議值為 Margin 或 TopUp。
    /// </summary>
    public BalanceAction? Action { get; init; }

    /// <summary>
    /// 僅當 Action=TopUp 時需要。
    /// </summary>
    public CurrencyTransactionType? TopUpTransactionType { get; init; }
}

internal readonly record struct StockBalanceDecisionValidationError(
    string FieldName,
    string Message,
    string? InvalidValue = null);

internal static class StockBalanceActionRules
{
    public const string FieldBalanceAction = "balanceAction";
    public const string FieldTopUpTransactionType = "topUpTransactionType";

    public static bool IsExecutableDefaultAction(BalanceAction? action)
        => action is BalanceAction.None or BalanceAction.Margin or BalanceAction.TopUp;

    public static bool IsImportTopUpIncomeType(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.Interest
            or CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.OtherIncome
            or CurrencyTransactionType.Deposit;


    public static StockBalanceDecisionValidationError? ValidateShortfallDecision(
        BalanceAction action,
        CurrencyTransactionType? topUpTransactionType)
    {
        if (!Enum.IsDefined(action) || action == BalanceAction.None)
        {
            return new StockBalanceDecisionValidationError(
                FieldBalanceAction,
                "帳本餘額不足，請選擇處理方式",
                action.ToString());
        }

        if (action == BalanceAction.TopUp)
        {
            if (!topUpTransactionType.HasValue)
            {
                return new StockBalanceDecisionValidationError(
                    FieldTopUpTransactionType,
                    "補足餘額需指定交易類型");
            }

            var resolvedTopUpTransactionType = topUpTransactionType.Value;
            if (!IsImportTopUpIncomeType(resolvedTopUpTransactionType))
            {
                return new StockBalanceDecisionValidationError(
                    FieldTopUpTransactionType,
                    "補足餘額的交易類型必須為入帳類型（限 Deposit / InitialBalance / Interest / OtherIncome）",
                    resolvedTopUpTransactionType.ToString());
            }
        }

        return null;
    }
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

    public BankAccountType AccountType { get; init; } = BankAccountType.Savings;
    public int? TermMonths { get; init; }
    public DateTime? StartDate { get; init; }
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

    public BankAccountType? AccountType { get; init; }
    public int? TermMonths { get; init; }
    public DateTime? StartDate { get; init; }
    public decimal? ActualInterest { get; init; }
    public FixedDepositStatus? FixedDepositStatus { get; init; }
}

/// <summary>
/// Close bank account request DTO.
/// </summary>
public record CloseBankAccountRequest
{
    [Range(0, double.MaxValue)]
    public decimal? ActualInterest { get; init; }
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
