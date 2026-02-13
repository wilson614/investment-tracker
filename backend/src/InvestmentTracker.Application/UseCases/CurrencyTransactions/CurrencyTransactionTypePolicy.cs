using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 外幣交易類型與帳本幣別的共用政策驗證。
/// 統一給 create/update/import flow 重用，避免規則分散。
/// </summary>
internal static class CurrencyTransactionTypePolicy
{
    private const string TwdCurrencyCode = "TWD";

    internal const string InvalidTransactionTypeForLedgerErrorCode = "INVALID_TRANSACTION_TYPE_FOR_LEDGER";
    internal const string RequiredFieldMissingErrorCode = "REQUIRED_FIELD_MISSING";

    /// <summary>
    /// 驗證帳本幣別與交易類型是否符合政策。
    /// amount/targetAmount 可選擇是否一併驗證（供 import flow 使用）。
    /// </summary>
    internal static CurrencyTransactionTypePolicyValidationResult Validate(
        string ledgerCurrencyCode,
        CurrencyTransactionType transactionType,
        CurrencyTransactionAmountPresence? amountPresence = null)
    {
        var diagnostics = new List<CurrencyTransactionTypePolicyDiagnostic>();

        if (!Enum.IsDefined(transactionType))
        {
            diagnostics.Add(BuildUndefinedTransactionTypeDiagnostic(transactionType));
            return new CurrencyTransactionTypePolicyValidationResult(false, diagnostics);
        }

        if (!IsAllowedForLedgerCurrency(ledgerCurrencyCode, transactionType))
        {
            diagnostics.Add(BuildTransactionTypeDiagnostic(ledgerCurrencyCode, transactionType));
        }

        if (amountPresence is not null)
        {
            var amountRequirement = GetAmountRequirement(transactionType);

            if (amountRequirement.RequiresAmount && !amountPresence.HasAmount)
            {
                diagnostics.Add(new CurrencyTransactionTypePolicyDiagnostic(
                    RequiredFieldMissingErrorCode,
                    FieldNames.Amount,
                    "此交易類型需要 amount",
                    "請填入大於 0 的 amount。"
                ));
            }

            if (amountRequirement.RequiresTargetAmount && !amountPresence.HasTargetAmount)
            {
                diagnostics.Add(new CurrencyTransactionTypePolicyDiagnostic(
                    RequiredFieldMissingErrorCode,
                    FieldNames.TargetAmount,
                    "此交易類型需要 targetAmount",
                    "請填入 targetAmount（create/update 可對應到 homeAmount）。"
                ));
            }
        }

        return diagnostics.Count == 0
            ? CurrencyTransactionTypePolicyValidationResult.Valid
            : new CurrencyTransactionTypePolicyValidationResult(false, diagnostics);
    }

    /// <summary>
    /// 建立 create/update flow 可直接拋出的一致業務錯誤。
    /// </summary>
    /// <exception cref="BusinessRuleException">政策驗證不通過時拋出。</exception>
    internal static void EnsureValidOrThrow(
        string ledgerCurrencyCode,
        CurrencyTransactionType transactionType,
        CurrencyTransactionAmountPresence? amountPresence = null)
    {
        var result = Validate(ledgerCurrencyCode, transactionType, amountPresence);

        if (result.IsValid)
            return;

        var first = result.Diagnostics[0];
        throw new BusinessRuleException($"{first.Message} {first.CorrectionGuidance}".Trim());
    }

    /// <summary>
    /// 取得交易類型對 amount/targetAmount 的需求。
    /// 注意：amount 可對應到 foreignAmount，targetAmount 可對應到 homeAmount。
    /// </summary>
    internal static CurrencyTransactionAmountRequirement GetAmountRequirement(
        CurrencyTransactionType transactionType)
    {
        return transactionType switch
        {
            CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell
                => new CurrencyTransactionAmountRequirement(RequiresAmount: true, RequiresTargetAmount: true),
            _ => new CurrencyTransactionAmountRequirement(RequiresAmount: true, RequiresTargetAmount: false)
        };
    }

    /// <summary>
    /// ledger-currency/type matrix（TWD / non-TWD）。
    /// </summary>
    internal static bool IsAllowedForLedgerCurrency(
        string ledgerCurrencyCode,
        CurrencyTransactionType transactionType)
    {
        // TWD 帳本不允許換匯類型；其餘帳本目前允許所有類型。
        // 後續 enum 語意擴充時，集中在此方法調整即可。
        if (string.Equals(ledgerCurrencyCode, TwdCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return transactionType is not CurrencyTransactionType.ExchangeBuy
                and not CurrencyTransactionType.ExchangeSell;
        }

        return true;
    }

    private static CurrencyTransactionTypePolicyDiagnostic BuildUndefinedTransactionTypeDiagnostic(
        CurrencyTransactionType transactionType)
    {
        return new CurrencyTransactionTypePolicyDiagnostic(
            InvalidTransactionTypeForLedgerErrorCode,
            FieldNames.TransactionType,
            "交易類型不符合此帳本規則",
            "請使用已定義的 transactionType。",
            transactionType.ToString());
    }

    private static CurrencyTransactionTypePolicyDiagnostic BuildTransactionTypeDiagnostic(
        string ledgerCurrencyCode,
        CurrencyTransactionType transactionType)
    {
        if (string.Equals(ledgerCurrencyCode, TwdCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return new CurrencyTransactionTypePolicyDiagnostic(
                InvalidTransactionTypeForLedgerErrorCode,
                FieldNames.TransactionType,
                "交易類型不符合此帳本規則",
                "TWD 帳本不可使用 ExchangeBuy/ExchangeSell，請改用此帳本允許的類型。",
                transactionType.ToString());
        }

        return new CurrencyTransactionTypePolicyDiagnostic(
            InvalidTransactionTypeForLedgerErrorCode,
            FieldNames.TransactionType,
            "交易類型不符合此帳本規則",
            "請確認帳本幣別與交易類型組合是否符合政策。",
            transactionType.ToString());
    }

    private static class FieldNames
    {
        internal const string TransactionType = "transactionType";
        internal const string Amount = "amount";
        internal const string TargetAmount = "targetAmount";
    }
}

/// <summary>
/// amount/targetAmount 是否有提供值。
/// </summary>
internal sealed record CurrencyTransactionAmountPresence(
    bool HasAmount,
    bool HasTargetAmount);

/// <summary>
/// 指定交易類型是否需要 amount/targetAmount。
/// </summary>
internal sealed record CurrencyTransactionAmountRequirement(
    bool RequiresAmount,
    bool RequiresTargetAmount);

/// <summary>
/// 可供 API/Import 回傳的政策診斷資訊。
/// </summary>
internal sealed record CurrencyTransactionTypePolicyDiagnostic(
    string ErrorCode,
    string FieldName,
    string Message,
    string CorrectionGuidance,
    string? InvalidValue = null);

/// <summary>
/// 政策驗證結果。
/// </summary>
internal sealed record CurrencyTransactionTypePolicyValidationResult(
    bool IsValid,
    IReadOnlyList<CurrencyTransactionTypePolicyDiagnostic> Diagnostics)
{
    internal static CurrencyTransactionTypePolicyValidationResult Valid { get; } =
        new(true, Array.Empty<CurrencyTransactionTypePolicyDiagnostic>());
}
