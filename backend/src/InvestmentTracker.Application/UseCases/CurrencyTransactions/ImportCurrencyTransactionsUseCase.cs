using System.Globalization;
using System.Text;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using CurrencyLedgerEntity = InvestmentTracker.Domain.Entities.CurrencyLedger;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// CSV 匯入外幣交易（Currency Transaction）的 Use Case。
/// 支援一次回傳完整錯誤集合（row/field/value/guidance），並保證 all-or-nothing 寫入。
/// </summary>
public interface IImportCurrencyTransactionsUseCase
{
    Task<CurrencyTransactionCsvImportResultDto> ExecuteAsync(
        ImportCurrencyTransactionsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ImportCurrencyTransactionsUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    IPortfolioRepository portfolioRepository,
    ITransactionPortfolioSnapshotService txSnapshotService,
    ICurrentUserService currentUserService,
    IAppDbTransactionManager transactionManager) : IImportCurrencyTransactionsUseCase
{
    private const string StatusCommitted = "committed";
    private const string StatusRejected = "rejected";

    private const int MaxImportCsvDataRows = 5000;

    private const string ErrorCodeCsvEmpty = "CSV_EMPTY";
    private const string ErrorCodeCsvNoDataRows = "CSV_NO_DATA_ROWS";
    private const string ErrorCodeCsvHeaderMissing = "CSV_HEADER_MISSING";
    private const string ErrorCodeCsvRowLimitExceeded = "CSV_ROW_LIMIT_EXCEEDED";
    private const string ErrorCodeInvalidDateFormat = "INVALID_DATE_FORMAT";
    private const string ErrorCodeInvalidNumberFormat = "INVALID_NUMBER_FORMAT";
    private const string ErrorCodeInvalidEnumValue = "INVALID_ENUM_VALUE";
    private const string ErrorCodeValueOutOfRange = "VALUE_OUT_OF_RANGE";
    private const string ErrorCodeFieldLengthExceeded = "FIELD_LENGTH_EXCEEDED";

    private static readonly string[] SupportedDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy/M/d",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss",
        "M/d/yyyy",
        "MM/dd/yyyy"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> HeaderAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldNames.TransactionDate] =
            [
                "transactionDate",
                "transaction_date",
                "date",
                "交易日期",
                "日期"
            ],
            [FieldNames.TransactionType] =
            [
                "transactionType",
                "transaction_type",
                "type",
                "transaction",
                "交易類型",
                "類型",
                "種類"
            ],
            [FieldNames.ForeignAmount] =
            [
                "foreignAmount",
                "foreign_amount",
                "amount",
                "外幣金額",
                "外幣",
                "金額"
            ],
            [FieldNames.HomeAmount] =
            [
                "homeAmount",
                "home_amount",
                "targetAmount",
                "target_amount",
                "台幣金額",
                "台幣",
                "twdAmount"
            ],
            [FieldNames.ExchangeRate] =
            [
                "exchangeRate",
                "exchange_rate",
                "rate",
                "匯率"
            ],
            [FieldNames.Notes] =
            [
                "notes",
                "memo",
                "description",
                "備註",
                "說明"
            ]
        };

    public async Task<CurrencyTransactionCsvImportResultDto> ExecuteAsync(
        ImportCurrencyTransactionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LedgerId == Guid.Empty)
            throw new ArgumentException("Ledger ID is required", nameof(request));

        ArgumentNullException.ThrowIfNull(request.CsvStream);

        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var ledger = await ledgerRepository.GetByIdAsync(request.LedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", request.LedgerId);

        if (ledger.UserId != userId)
            throw new AccessDeniedException();

        var csvContent = await ReadCsvContentAsync(request.CsvStream, cancellationToken);
        var parseResult = ParseAndValidate(csvContent, ledger, MaxImportCsvDataRows);

        if (parseResult.Errors.Count > 0)
        {
            return BuildRejectedResult(parseResult.TotalRows, parseResult.Errors);
        }

        var boundPortfolios = (await portfolioRepository.GetByUserIdAsync(userId, cancellationToken))
            .Where(p => p.BoundCurrencyLedgerId == ledger.Id)
            .ToList();

        await using var tx = await transactionManager.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var row in parseResult.ValidRows)
            {
                var (homeAmount, exchangeRate) = ResolveAmountsForLedger(ledger, row);

                var transaction = new CurrencyTransaction(
                    ledger.Id,
                    row.TransactionDate,
                    row.TransactionType,
                    row.ForeignAmount,
                    homeAmount,
                    exchangeRate,
                    relatedStockTransactionId: null,
                    row.Notes);

                await transactionRepository.AddAsync(transaction, cancellationToken);

                if (IsExternalCashFlowType(transaction.TransactionType))
                {
                    foreach (var portfolio in boundPortfolios)
                    {
                        await txSnapshotService.UpsertSnapshotAsync(
                            portfolio.Id,
                            transaction.Id,
                            transaction.TransactionDate,
                            cancellationToken);
                    }
                }
            }

            await tx.CommitAsync(cancellationToken);

            return new CurrencyTransactionCsvImportResultDto
            {
                Status = StatusCommitted,
                Summary = new CurrencyTransactionCsvImportSummaryDto
                {
                    TotalRows = parseResult.TotalRows,
                    InsertedRows = parseResult.TotalRows,
                    RejectedRows = 0,
                    ErrorCount = null
                },
                Errors = []
            };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static CurrencyTransactionCsvImportResultDto BuildRejectedResult(
        int totalRows,
        IReadOnlyList<CurrencyTransactionCsvImportErrorDto> errors)
    {
        var sortedErrors = errors
            .OrderBy(e => e.RowNumber)
            .ThenBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CurrencyTransactionCsvImportResultDto
        {
            Status = StatusRejected,
            Summary = new CurrencyTransactionCsvImportSummaryDto
            {
                TotalRows = totalRows,
                InsertedRows = 0,
                RejectedRows = totalRows,
                ErrorCount = sortedErrors.Count
            },
            Errors = sortedErrors
        };
    }

    private static (decimal? HomeAmount, decimal? ExchangeRate) ResolveAmountsForLedger(
        CurrencyLedgerEntity ledger,
        ValidatedImportRow row)
    {
        if (string.Equals(ledger.CurrencyCode, ledger.HomeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return (row.ForeignAmount, 1.0m);
        }

        return (row.HomeAmount, row.ExchangeRate);
    }

    private static bool IsExternalCashFlowType(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.Deposit
            or CurrencyTransactionType.Withdraw;

    private static async Task<string> ReadCsvContentAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        if (csvStream.CanSeek)
        {
            csvStream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(
            csvStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        var content = await reader.ReadToEndAsync(cancellationToken);
        return content.TrimStart('\uFEFF');
    }

    private static ParseResult ParseAndValidate(string csvContent, CurrencyLedgerEntity ledger, int maxDataRows)
    {
        var rows = ParseCsvRows(csvContent);
        if (rows.Count == 0)
        {
            return new ParseResult(
                TotalRows: 0,
                ValidRows: [],
                Errors:
                [
                    new CurrencyTransactionCsvImportErrorDto
                    {
                        RowNumber = 1,
                        FieldName = FieldNames.File,
                        InvalidValue = string.Empty,
                        ErrorCode = ErrorCodeCsvEmpty,
                        Message = "CSV 內容為空",
                        CorrectionGuidance = "請上傳包含表頭與資料列的 CSV 檔案。"
                    }
                ]);
        }

        var headerIndex = rows.FindIndex(row => !IsBlankRow(row) && !IsCommentRow(row));
        if (headerIndex < 0)
        {
            return new ParseResult(
                TotalRows: 0,
                ValidRows: [],
                Errors:
                [
                    new CurrencyTransactionCsvImportErrorDto
                    {
                        RowNumber = 1,
                        FieldName = FieldNames.File,
                        InvalidValue = string.Empty,
                        ErrorCode = ErrorCodeCsvEmpty,
                        Message = "CSV 內容為空",
                        CorrectionGuidance = "請上傳包含表頭與資料列的 CSV 檔案。"
                    }
                ]);
        }

        var headerRow = rows[headerIndex];
        var dataRows = rows
            .Skip(headerIndex + 1)
            .Where(row => !IsBlankRow(row) && !IsCommentRow(row))
            .ToList();

        var totalRows = dataRows.Count;

        if (!TryBuildColumnMapping(headerRow, out var mapping, out var headerErrors))
        {
            return new ParseResult(totalRows, [], headerErrors);
        }

        if (totalRows == 0)
        {
            return new ParseResult(
                TotalRows: 0,
                ValidRows: [],
                Errors:
                [
                    new CurrencyTransactionCsvImportErrorDto
                    {
                        RowNumber = headerRow.RowNumber,
                        FieldName = FieldNames.File,
                        InvalidValue = string.Empty,
                        ErrorCode = ErrorCodeCsvNoDataRows,
                        Message = "CSV 沒有可匯入的資料列",
                        CorrectionGuidance = "請至少提供一筆交易資料列。"
                    }
                ]);
        }

        if (totalRows > maxDataRows)
        {
            return new ParseResult(
                TotalRows: totalRows,
                ValidRows: [],
                Errors:
                [
                    new CurrencyTransactionCsvImportErrorDto
                    {
                        RowNumber = headerRow.RowNumber,
                        FieldName = FieldNames.File,
                        InvalidValue = totalRows.ToString(CultureInfo.InvariantCulture),
                        ErrorCode = ErrorCodeCsvRowLimitExceeded,
                        Message = $"CSV 資料列超過上限（最多 {maxDataRows} 列）",
                        CorrectionGuidance = $"請將資料拆分後重試，每次最多匯入 {maxDataRows} 列。"
                    }
                ]);
        }

        var validRows = new List<ValidatedImportRow>();
        var diagnostics = new List<CurrencyTransactionCsvImportErrorDto>();

        foreach (var row in dataRows)
        {
            var rowValidation = ValidateDataRow(row, mapping, ledger);
            if (rowValidation.Errors.Count > 0)
            {
                diagnostics.AddRange(rowValidation.Errors);
                continue;
            }

            validRows.Add(rowValidation.Row!);
        }

        return new ParseResult(totalRows, validRows, diagnostics);
    }

    private static RowValidationResult ValidateDataRow(
        CsvRow row,
        CsvColumnMapping mapping,
        CurrencyLedgerEntity ledger)
    {
        var errors = new List<CurrencyTransactionCsvImportErrorDto>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rawDate = GetColumnValue(row.Columns, mapping.TransactionDateIndex);
        var rawType = GetColumnValue(row.Columns, mapping.TransactionTypeIndex);
        var rawForeignAmount = GetColumnValue(row.Columns, mapping.ForeignAmountIndex);
        var rawHomeAmount = GetColumnValue(row.Columns, mapping.HomeAmountIndex);
        var rawExchangeRate = GetColumnValue(row.Columns, mapping.ExchangeRateIndex);
        var rawNotes = GetColumnValue(row.Columns, mapping.NotesIndex);

        DateTime? transactionDate = null;
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.TransactionDate,
                rawDate,
                CurrencyTransactionTypePolicy.RequiredFieldMissingErrorCode,
                "交易日期為必填欄位",
                "請填入交易日期，例如 2026-02-13。"
            );
        }
        else if (!TryParseDate(rawDate, out var parsedDate))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.TransactionDate,
                rawDate,
                ErrorCodeInvalidDateFormat,
                "交易日期格式錯誤",
                "請使用可解析的日期格式（建議 yyyy-MM-dd）。"
            );
        }
        else if (parsedDate > DateTime.UtcNow.AddDays(1))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.TransactionDate,
                rawDate,
                ErrorCodeValueOutOfRange,
                "交易日期不可晚於未來時間",
                "請填入今天或更早的日期。"
            );
        }
        else
        {
            transactionDate = parsedDate;
        }

        CurrencyTransactionType? transactionType = null;
        if (string.IsNullOrWhiteSpace(rawType))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.TransactionType,
                rawType,
                CurrencyTransactionTypePolicy.RequiredFieldMissingErrorCode,
                "交易類型為必填欄位",
                "請填入有效的 transactionType。"
            );
        }
        else if (!TryParseTransactionType(rawType, out var parsedType))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.TransactionType,
                rawType,
                ErrorCodeInvalidEnumValue,
                "交易類型不合法",
                "請使用 API 定義中的 transactionType（可用名稱或對應數值）。"
            );
        }
        else
        {
            transactionType = parsedType;
        }

        decimal? foreignAmount = null;
        if (string.IsNullOrWhiteSpace(rawForeignAmount))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.ForeignAmount,
                rawForeignAmount,
                CurrencyTransactionTypePolicy.RequiredFieldMissingErrorCode,
                "外幣金額為必填欄位",
                "請填入大於 0 的 foreignAmount。"
            );
        }
        else if (!TryParseDecimal(rawForeignAmount, out var parsedForeignAmount))
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.ForeignAmount,
                rawForeignAmount,
                ErrorCodeInvalidNumberFormat,
                "外幣金額格式錯誤",
                "請填入可解析的數值。"
            );
        }
        else if (parsedForeignAmount <= 0)
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.ForeignAmount,
                rawForeignAmount,
                ErrorCodeValueOutOfRange,
                "外幣金額必須大於 0",
                "請填入大於 0 的 foreignAmount。"
            );
        }
        else
        {
            foreignAmount = parsedForeignAmount;
        }

        decimal? homeAmount = null;
        if (!string.IsNullOrWhiteSpace(rawHomeAmount))
        {
            if (!TryParseDecimal(rawHomeAmount, out var parsedHomeAmount))
            {
                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    FieldNames.HomeAmount,
                    rawHomeAmount,
                    ErrorCodeInvalidNumberFormat,
                    "本位幣金額格式錯誤",
                    "請填入可解析的數值。"
                );
            }
            else if (parsedHomeAmount <= 0)
            {
                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    FieldNames.HomeAmount,
                    rawHomeAmount,
                    ErrorCodeValueOutOfRange,
                    "本位幣金額必須大於 0",
                    "請填入大於 0 的 homeAmount。"
                );
            }
            else
            {
                homeAmount = parsedHomeAmount;
            }
        }

        decimal? exchangeRate = null;
        if (!string.IsNullOrWhiteSpace(rawExchangeRate))
        {
            if (!TryParseDecimal(rawExchangeRate, out var parsedExchangeRate))
            {
                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    FieldNames.ExchangeRate,
                    rawExchangeRate,
                    ErrorCodeInvalidNumberFormat,
                    "匯率格式錯誤",
                    "請填入可解析的數值。"
                );
            }
            else if (parsedExchangeRate <= 0)
            {
                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    FieldNames.ExchangeRate,
                    rawExchangeRate,
                    ErrorCodeValueOutOfRange,
                    "匯率必須大於 0",
                    "請填入大於 0 的 exchangeRate。"
                );
            }
            else
            {
                exchangeRate = parsedExchangeRate;
            }
        }

        var notes = string.IsNullOrWhiteSpace(rawNotes) ? null : rawNotes.Trim();
        if (notes is not null && notes.Length > 500)
        {
            AddDiagnostic(
                errors,
                dedupe,
                row.RowNumber,
                FieldNames.Notes,
                rawNotes,
                ErrorCodeFieldLengthExceeded,
                "備註長度不可超過 500 字元",
                "請縮短 notes 長度至 500 字元以內。"
            );
        }

        if (transactionType.HasValue)
        {
            var policyResult = CurrencyTransactionTypePolicy.Validate(
                ledger.CurrencyCode,
                transactionType.Value,
                new CurrencyTransactionAmountPresence(
                    HasAmount: !string.IsNullOrWhiteSpace(rawForeignAmount),
                    HasTargetAmount: !string.IsNullOrWhiteSpace(rawHomeAmount)));

            foreach (var diagnostic in policyResult.Diagnostics)
            {
                var mappedField = MapPolicyFieldName(diagnostic.FieldName);
                var invalidValue = ResolvePolicyInvalidValue(
                    mappedField,
                    diagnostic.InvalidValue,
                    rawType,
                    rawForeignAmount,
                    rawHomeAmount);

                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    mappedField,
                    invalidValue,
                    diagnostic.ErrorCode,
                    diagnostic.Message,
                    diagnostic.CorrectionGuidance);
            }

            if (RequiresExchangeRate(transactionType.Value) && string.IsNullOrWhiteSpace(rawExchangeRate))
            {
                AddDiagnostic(
                    errors,
                    dedupe,
                    row.RowNumber,
                    FieldNames.ExchangeRate,
                    rawExchangeRate,
                    CurrencyTransactionTypePolicy.RequiredFieldMissingErrorCode,
                    "此交易類型需要 exchangeRate",
                    "請填入大於 0 的 exchangeRate。"
                );
            }
        }

        if (errors.Count > 0 ||
            !transactionDate.HasValue ||
            !transactionType.HasValue ||
            !foreignAmount.HasValue)
        {
            return new RowValidationResult(null, errors);
        }

        return new RowValidationResult(
            new ValidatedImportRow(
                row.RowNumber,
                transactionDate.Value,
                transactionType.Value,
                foreignAmount.Value,
                homeAmount,
                exchangeRate,
                notes),
            errors);
    }

    private static string MapPolicyFieldName(string fieldName)
        => fieldName switch
        {
            "amount" => FieldNames.ForeignAmount,
            "targetAmount" => FieldNames.HomeAmount,
            _ => fieldName
        };

    private static string ResolvePolicyInvalidValue(
        string mappedField,
        string? policyInvalidValue,
        string rawType,
        string rawForeignAmount,
        string rawHomeAmount)
    {
        if (!string.IsNullOrWhiteSpace(policyInvalidValue))
            return policyInvalidValue;

        return mappedField switch
        {
            FieldNames.TransactionType => rawType,
            FieldNames.ForeignAmount => rawForeignAmount,
            FieldNames.HomeAmount => rawHomeAmount,
            _ => string.Empty
        };
    }

    private static bool RequiresExchangeRate(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell;

    private static bool TryBuildColumnMapping(
        CsvRow headerRow,
        out CsvColumnMapping mapping,
        out List<CurrencyTransactionCsvImportErrorDto> errors)
    {
        errors = [];
        mapping = default!;

        int? transactionDateIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.TransactionDate]);
        int? transactionTypeIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.TransactionType]);
        int? foreignAmountIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.ForeignAmount]);

        int? homeAmountIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.HomeAmount]);
        int? exchangeRateIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.ExchangeRate]);
        int? notesIndex = FindHeaderIndex(headerRow.Columns, HeaderAliases[FieldNames.Notes]);

        AddMissingHeaderErrorIfNeeded(headerRow, errors, FieldNames.TransactionDate, transactionDateIndex);
        AddMissingHeaderErrorIfNeeded(headerRow, errors, FieldNames.TransactionType, transactionTypeIndex);
        AddMissingHeaderErrorIfNeeded(headerRow, errors, FieldNames.ForeignAmount, foreignAmountIndex);

        if (errors.Count > 0)
        {
            return false;
        }

        mapping = new CsvColumnMapping(
            transactionDateIndex!.Value,
            transactionTypeIndex!.Value,
            foreignAmountIndex!.Value,
            homeAmountIndex,
            exchangeRateIndex,
            notesIndex);

        return true;
    }

    private static void AddMissingHeaderErrorIfNeeded(
        CsvRow headerRow,
        List<CurrencyTransactionCsvImportErrorDto> errors,
        string fieldName,
        int? headerIndex)
    {
        if (headerIndex.HasValue)
            return;

        errors.Add(new CurrencyTransactionCsvImportErrorDto
        {
            RowNumber = headerRow.RowNumber,
            FieldName = fieldName,
            InvalidValue = string.Join(",", headerRow.Columns),
            ErrorCode = ErrorCodeCsvHeaderMissing,
            Message = $"CSV 缺少必要欄位：{fieldName}",
            CorrectionGuidance = $"請在表頭加入欄位「{fieldName}」。"
        });
    }

    private static int? FindHeaderIndex(IReadOnlyList<string> headers, IReadOnlyList<string> aliases)
    {
        var aliasSet = aliases
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < headers.Count; index++)
        {
            if (aliasSet.Contains(NormalizeHeader(headers[index])))
                return index;
        }

        return null;
    }

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string GetColumnValue(IReadOnlyList<string> columns, int? index)
    {
        if (!index.HasValue || index.Value < 0 || index.Value >= columns.Count)
            return string.Empty;

        return columns[index.Value].Trim();
    }

    private static bool TryParseTransactionType(string rawValue, out CurrencyTransactionType transactionType)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            var numericType = (CurrencyTransactionType)numeric;
            if (Enum.IsDefined(numericType))
            {
                transactionType = numericType;
                return true;
            }
        }

        if (Enum.TryParse<CurrencyTransactionType>(rawValue, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            transactionType = parsed;
            return true;
        }

        transactionType = default;
        return false;
    }

    private static bool TryParseDate(string rawValue, out DateTime value)
    {
        if (DateTime.TryParseExact(
                rawValue,
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed) ||
            DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed) ||
            DateTime.TryParse(
                rawValue,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            value = parsed.Date;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimal(string rawValue, out decimal value)
        => decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
           || decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static void AddDiagnostic(
        List<CurrencyTransactionCsvImportErrorDto> errors,
        ISet<string> dedupe,
        int rowNumber,
        string fieldName,
        string? invalidValue,
        string errorCode,
        string message,
        string correctionGuidance)
    {
        var normalizedInvalidValue = invalidValue ?? string.Empty;
        var key = $"{rowNumber}|{fieldName}|{errorCode}|{normalizedInvalidValue}";
        if (!dedupe.Add(key))
            return;

        errors.Add(new CurrencyTransactionCsvImportErrorDto
        {
            RowNumber = rowNumber,
            FieldName = fieldName,
            InvalidValue = normalizedInvalidValue,
            ErrorCode = errorCode,
            Message = message,
            CorrectionGuidance = correctionGuidance
        });
    }

    private static bool IsBlankRow(CsvRow row)
        => row.Columns.Count == 0 || row.Columns.All(string.IsNullOrWhiteSpace);

    private static bool IsCommentRow(CsvRow row)
    {
        if (row.Columns.Count == 0)
            return false;

        return row.Columns[0].TrimStart().StartsWith('#');
    }

    private static List<CsvRow> ParseCsvRows(string csvContent)
    {
        var content = (csvContent ?? string.Empty).TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var rows = new List<CsvRow>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var currentRowNumber = 1;

        for (var i = 0; i < content.Length; i++)
        {
            var character = content[i];

            if (character == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == ',')
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            if (!inQuotes && (character == '\r' || character == '\n'))
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();

                rows.Add(new CsvRow(currentRowNumber, currentRow));
                currentRow = [];
                currentRowNumber++;

                if (character == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            currentField.Append(character);
        }

        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            rows.Add(new CsvRow(currentRowNumber, currentRow));
        }

        return rows;
    }

    private static class FieldNames
    {
        internal const string File = "file";
        internal const string TransactionDate = "transactionDate";
        internal const string TransactionType = "transactionType";
        internal const string ForeignAmount = "foreignAmount";
        internal const string HomeAmount = "homeAmount";
        internal const string ExchangeRate = "exchangeRate";
        internal const string Notes = "notes";
    }

    private sealed record CsvRow(
        int RowNumber,
        IReadOnlyList<string> Columns);

    private sealed record CsvColumnMapping(
        int TransactionDateIndex,
        int TransactionTypeIndex,
        int ForeignAmountIndex,
        int? HomeAmountIndex,
        int? ExchangeRateIndex,
        int? NotesIndex);

    private sealed record ValidatedImportRow(
        int RowNumber,
        DateTime TransactionDate,
        CurrencyTransactionType TransactionType,
        decimal ForeignAmount,
        decimal? HomeAmount,
        decimal? ExchangeRate,
        string? Notes);

    private sealed record ParseResult(
        int TotalRows,
        IReadOnlyList<ValidatedImportRow> ValidRows,
        IReadOnlyList<CurrencyTransactionCsvImportErrorDto> Errors);

    private sealed record RowValidationResult(
        ValidatedImportRow? Row,
        IReadOnlyList<CurrencyTransactionCsvImportErrorDto> Errors);
}

