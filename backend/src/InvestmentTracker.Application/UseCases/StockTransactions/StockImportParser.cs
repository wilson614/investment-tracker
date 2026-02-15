using System.Globalization;
using System.Text;
using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

public interface IStockImportParser
{
    StockImportParseResult Parse(string csvContent, string selectedFormat);
}

public sealed class StockImportParser : IStockImportParser
{
    private const int MaxImportCsvDataRows = 5000;

    internal const string FormatLegacyCsv = "legacy_csv";
    internal const string FormatBrokerStatement = "broker_statement";
    internal const string FormatUnknown = "unknown";

    internal const string TradeSideBuy = "buy";
    internal const string TradeSideSell = "sell";
    internal const string TradeSideAmbiguous = "ambiguous";

    internal const string ActionConfirmTradeSide = "confirm_trade_side";

    private const string ErrorCodeCsvEmpty = "CSV_EMPTY";
    private const string ErrorCodeCsvNoDataRows = "CSV_NO_DATA_ROWS";
    private const string ErrorCodeCsvHeaderMissing = "CSV_HEADER_MISSING";
    private const string ErrorCodeCsvRowLimitExceeded = "CSV_ROW_LIMIT_EXCEEDED";
    private const string ErrorCodeRequiredFieldMissing = "REQUIRED_FIELD_MISSING";
    private const string ErrorCodeInvalidDateFormat = "INVALID_DATE_FORMAT";
    private const string ErrorCodeInvalidNumberFormat = "INVALID_NUMBER_FORMAT";
    private const string ErrorCodeInvalidEnumValue = "INVALID_ENUM_VALUE";
    private const string ErrorCodeValueOutOfRange = "VALUE_OUT_OF_RANGE";
    private const string ErrorCodeUnsupportedTradeType = "UNSUPPORTED_TRADE_TYPE";
    private const string ErrorCodeTradeSideAmbiguous = "TRADE_SIDE_AMBIGUOUS";

    private static readonly string[] SupportedDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy/M/d",
        "yyyy.MM.dd",
        "yyyy.M.d"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BrokerHeaderAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [FieldNames.RawSecurityName] = ["股名", "證券名稱", "股票名稱", "securityName", "name"],
            [FieldNames.Ticker] = ["ticker", "symbol", "股票代號", "證券代號", "代號"],
            [FieldNames.TradeDate] = ["日期", "成交日期", "交易日期", "tradeDate"],
            [FieldNames.Quantity] = ["成交股數", "股數", "成交數量", "數量", "quantity"],
            [FieldNames.NetSettlement] = ["淨收付", "淨收付金額", "netSettlement"],
            [FieldNames.UnitPrice] = ["成交單價", "單價", "price", "unitPrice"],
            [FieldNames.Fees] = ["手續費", "fee", "fees", "commission"],
            [FieldNames.Taxes] = ["交易稅", "稅款", "tax", "taxes"],
            [FieldNames.Currency] = ["幣別", "貨幣", "currency", "currencyCode"]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyHeaderAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [FieldNames.TradeDate] = ["date", "transactionDate", "transaction_date", "交易日期", "日期", "買進日期"],
            [FieldNames.Ticker] = ["ticker", "symbol", "stock", "代碼", "股票", "股票代號"],
            [FieldNames.TradeType] = ["transactionType", "transaction_type", "type", "類型", "買賣"],
            [FieldNames.Quantity] = ["shares", "quantity", "qty", "股數", "數量", "股", "買進股數"],
            [FieldNames.UnitPrice] = ["pricePerShare", "price_per_share", "price", "價格", "單價", "買進價格"],
            [FieldNames.Fees] = ["fees", "fee", "commission", "手續費", "費用"],
            [FieldNames.Currency] = ["currency", "currencyCode", "幣別", "貨幣"]
        };

    public StockImportParseResult Parse(string csvContent, string selectedFormat)
    {
        ArgumentNullException.ThrowIfNull(csvContent);

        var selected = NormalizeSelectedFormat(selectedFormat);

        var allRows = ParseCsvRows(csvContent);
        if (allRows.Count == 0)
        {
            return new StockImportParseResult(
                DetectedFormat: FormatUnknown,
                SelectedFormat: selected,
                Rows: [],
                Diagnostics:
                [
                    CreateDiagnostic(
                        rowNumber: 1,
                        fieldName: FieldNames.File,
                        invalidValue: string.Empty,
                        errorCode: ErrorCodeCsvEmpty,
                        message: "CSV 內容為空",
                        correctionGuidance: "請上傳包含表頭與資料列的 CSV 檔案。")
                ]);
        }

        var headerIndex = allRows.FindIndex(row => !IsBlankRow(row));
        if (headerIndex < 0)
        {
            return new StockImportParseResult(
                DetectedFormat: FormatUnknown,
                SelectedFormat: selected,
                Rows: [],
                Diagnostics:
                [
                    CreateDiagnostic(
                        rowNumber: 1,
                        fieldName: FieldNames.File,
                        invalidValue: string.Empty,
                        errorCode: ErrorCodeCsvEmpty,
                        message: "CSV 內容為空",
                        correctionGuidance: "請上傳包含表頭與資料列的 CSV 檔案。")
                ]);
        }

        var header = allRows[headerIndex];
        var dataRows = allRows
            .Skip(headerIndex + 1)
            .Where(row => !IsBlankRow(row))
            .ToList();

        var detectedFormat = DetectFormat(header.Columns);

        if (dataRows.Count == 0)
        {
            return new StockImportParseResult(
                DetectedFormat: detectedFormat,
                SelectedFormat: selected,
                Rows: [],
                Diagnostics:
                [
                    CreateDiagnostic(
                        rowNumber: 1,
                        fieldName: FieldNames.File,
                        invalidValue: string.Empty,
                        errorCode: ErrorCodeCsvNoDataRows,
                        message: "CSV 沒有可匯入的資料列",
                        correctionGuidance: "請至少提供一筆交易資料列。")
                ]);
        }

        if (dataRows.Count > MaxImportCsvDataRows)
        {
            return new StockImportParseResult(
                DetectedFormat: detectedFormat,
                SelectedFormat: selected,
                Rows: [],
                Diagnostics:
                [
                    CreateDiagnostic(
                        rowNumber: 1,
                        fieldName: FieldNames.File,
                        invalidValue: dataRows.Count.ToString(CultureInfo.InvariantCulture),
                        errorCode: ErrorCodeCsvRowLimitExceeded,
                        message: $"CSV 資料列超過上限（最多 {MaxImportCsvDataRows} 列）",
                        correctionGuidance: $"請將資料拆分後重試，每次最多匯入 {MaxImportCsvDataRows} 列。")
                ]);
        }

        return selected switch
        {
            FormatBrokerStatement => ParseBrokerStatement(detectedFormat, selected, header, dataRows),
            FormatLegacyCsv => ParseLegacyCsv(detectedFormat, selected, header, dataRows),
            _ => new StockImportParseResult(
                DetectedFormat: detectedFormat,
                SelectedFormat: selected,
                Rows: [],
                Diagnostics:
                [
                    CreateDiagnostic(
                        rowNumber: 1,
                        fieldName: FieldNames.SelectedFormat,
                        invalidValue: selectedFormat,
                        errorCode: ErrorCodeInvalidEnumValue,
                        message: "selectedFormat 無效",
                        correctionGuidance: "請使用 legacy_csv 或 broker_statement。")
                ])
        };
    }

    private static StockImportParseResult ParseBrokerStatement(
        string detectedFormat,
        string selectedFormat,
        CsvRow header,
        IReadOnlyList<CsvRow> dataRows)
    {
        var diagnostics = new List<StockImportDiagnosticDto>();

        if (!TryBuildBrokerColumnMapping(header, out var mapping, out var headerDiagnostics))
        {
            diagnostics.AddRange(headerDiagnostics);
            return new StockImportParseResult(detectedFormat, selectedFormat, [], diagnostics);
        }

        var rows = new List<StockImportParsedRow>(dataRows.Count);
        for (var i = 0; i < dataRows.Count; i++)
        {
            rows.Add(ParseBrokerRow(i + 1, dataRows[i], mapping, diagnostics));
        }

        return new StockImportParseResult(detectedFormat, selectedFormat, rows, diagnostics.OrderBy(d => d.RowNumber).ToList());
    }

    private static StockImportParseResult ParseLegacyCsv(
        string detectedFormat,
        string selectedFormat,
        CsvRow header,
        IReadOnlyList<CsvRow> dataRows)
    {
        var diagnostics = new List<StockImportDiagnosticDto>();

        if (!TryBuildLegacyColumnMapping(header, out var mapping, out var headerDiagnostics))
        {
            diagnostics.AddRange(headerDiagnostics);
            return new StockImportParseResult(detectedFormat, selectedFormat, [], diagnostics);
        }

        var rows = new List<StockImportParsedRow>(dataRows.Count);
        for (var i = 0; i < dataRows.Count; i++)
        {
            rows.Add(ParseLegacyRow(i + 1, dataRows[i], mapping, diagnostics));
        }

        return new StockImportParseResult(detectedFormat, selectedFormat, rows, diagnostics.OrderBy(d => d.RowNumber).ToList());
    }

    private static StockImportParsedRow ParseBrokerRow(
        int rowNumber,
        CsvRow csvRow,
        BrokerColumnMapping mapping,
        List<StockImportDiagnosticDto> diagnostics)
    {
        var blockingError = false;
        var actionsRequired = new List<string>();

        var rawSecurityName = NormalizeSecurityName(GetColumnValue(csvRow.Columns, mapping.RawSecurityNameIndex));
        var ticker = NormalizeTicker(GetColumnValue(csvRow.Columns, mapping.TickerIndex));
        var rawDate = GetColumnValue(csvRow.Columns, mapping.TradeDateIndex);
        var rawQuantity = GetColumnValue(csvRow.Columns, mapping.QuantityIndex);
        var rawUnitPrice = GetColumnValue(csvRow.Columns, mapping.UnitPriceIndex);
        var rawNetSettlement = GetColumnValue(csvRow.Columns, mapping.NetSettlementIndex);
        var rawFees = GetColumnValue(csvRow.Columns, mapping.FeesIndex);
        var rawTaxes = GetColumnValue(csvRow.Columns, mapping.TaxesIndex);
        var rawCurrency = GetColumnValue(csvRow.Columns, mapping.CurrencyIndex);

        if (string.IsNullOrWhiteSpace(rawSecurityName) && string.IsNullOrWhiteSpace(ticker))
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.RawSecurityName,
                null,
                ErrorCodeRequiredFieldMissing,
                "缺少可識別標的資訊",
                "請提供股名或股票代號。",
                ref blockingError,
                isBlocking: true);
        }

        var tradeDate = ParseDate(
            rowNumber,
            FieldNames.TradeDate,
            rawDate,
            diagnostics,
            ref blockingError,
            required: true);

        var quantity = ParseDecimal(
            rowNumber,
            FieldNames.Quantity,
            rawQuantity,
            diagnostics,
            ref blockingError,
            required: true,
            allowNegative: false);

        if (quantity is <= 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Quantity,
                rawQuantity,
                ErrorCodeValueOutOfRange,
                "成交股數必須大於 0",
                "請提供大於 0 的成交股數。",
                ref blockingError,
                isBlocking: true);
            blockingError = true;
        }

        var unitPrice = ParseDecimal(
            rowNumber,
            FieldNames.UnitPrice,
            rawUnitPrice,
            diagnostics,
            ref blockingError,
            required: true,
            allowNegative: false);

        if (unitPrice is < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.UnitPrice,
                rawUnitPrice,
                ErrorCodeValueOutOfRange,
                "成交單價不可為負數",
                "請提供大於或等於 0 的成交單價。",
                ref blockingError,
                isBlocking: true);
        }

        var netSettlement = ParseDecimal(
            rowNumber,
            FieldNames.NetSettlement,
            rawNetSettlement,
            diagnostics,
            ref blockingError,
            required: false,
            allowNegative: true,
            isBlockingOnParseFailure: false);

        var fees = ParseDecimal(
            rowNumber,
            FieldNames.Fees,
            rawFees,
            diagnostics,
            ref blockingError,
            required: false,
            allowNegative: false) ?? 0m;

        if (fees < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Fees,
                rawFees,
                ErrorCodeValueOutOfRange,
                "手續費不可為負數",
                "請提供大於或等於 0 的手續費。",
                ref blockingError,
                isBlocking: true);
            fees = 0m;
        }

        var taxes = ParseDecimal(
            rowNumber,
            FieldNames.Taxes,
            rawTaxes,
            diagnostics,
            ref blockingError,
            required: false,
            allowNegative: false) ?? 0m;

        if (taxes < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Taxes,
                rawTaxes,
                ErrorCodeValueOutOfRange,
                "稅款不可為負數",
                "請提供大於或等於 0 的稅款。",
                ref blockingError,
                isBlocking: true);
            taxes = 0m;
        }

        var currency = ParseCurrency(
            rowNumber,
            rawCurrency,
            diagnostics,
            ref blockingError,
            isBlockingOnUnknown: true);

        var tradeSide = DeriveTradeSide(netSettlement);
        if (tradeSide == TradeSideAmbiguous)
        {
            if (!actionsRequired.Contains(ActionConfirmTradeSide, StringComparer.Ordinal))
            {
                actionsRequired.Add(ActionConfirmTradeSide);
            }

            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.TradeSide,
                rawNetSettlement,
                ErrorCodeTradeSideAmbiguous,
                "無法自動判斷買賣方向",
                "請手動確認此列為買入或賣出。",
                ref blockingError,
                isBlocking: false);
        }

        return new StockImportParsedRow(
            RowNumber: rowNumber,
            TradeDate: tradeDate,
            RawSecurityName: rawSecurityName,
            Ticker: ticker,
            TradeSide: tradeSide,
            Quantity: quantity,
            UnitPrice: unitPrice,
            Fees: fees,
            Taxes: taxes,
            NetSettlement: netSettlement,
            Currency: currency,
            IsInvalid: blockingError,
            ActionsRequired: actionsRequired);
    }

    private static StockImportParsedRow ParseLegacyRow(
        int rowNumber,
        CsvRow csvRow,
        LegacyColumnMapping mapping,
        List<StockImportDiagnosticDto> diagnostics)
    {
        var blockingError = false;

        var rawDate = GetColumnValue(csvRow.Columns, mapping.TradeDateIndex);
        var rawTicker = GetColumnValue(csvRow.Columns, mapping.TickerIndex);
        var rawTradeType = GetColumnValue(csvRow.Columns, mapping.TradeTypeIndex);
        var rawQuantity = GetColumnValue(csvRow.Columns, mapping.QuantityIndex);
        var rawUnitPrice = GetColumnValue(csvRow.Columns, mapping.UnitPriceIndex);
        var rawFees = GetColumnValue(csvRow.Columns, mapping.FeesIndex);
        var rawCurrency = GetColumnValue(csvRow.Columns, mapping.CurrencyIndex);

        var tradeDate = ParseDate(
            rowNumber,
            FieldNames.TradeDate,
            rawDate,
            diagnostics,
            ref blockingError,
            required: true);

        var ticker = NormalizeTicker(rawTicker);
        if (string.IsNullOrWhiteSpace(ticker))
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Ticker,
                rawTicker,
                ErrorCodeRequiredFieldMissing,
                "缺少股票代號",
                "請提供股票代號。",
                ref blockingError,
                isBlocking: true);
        }

        var tradeSide = ParseLegacyTradeSide(rawTradeType);
        if (tradeSide is null)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.TradeType,
                rawTradeType,
                ErrorCodeUnsupportedTradeType,
                "此匯入流程僅支援買入/賣出交易",
                "請將交易類型調整為 buy/sell（或買/賣）。",
                ref blockingError,
                isBlocking: true);
        }

        var quantity = ParseDecimal(
            rowNumber,
            FieldNames.Quantity,
            rawQuantity,
            diagnostics,
            ref blockingError,
            required: true,
            allowNegative: false);

        if (quantity is <= 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Quantity,
                rawQuantity,
                ErrorCodeValueOutOfRange,
                "股數必須大於 0",
                "請提供大於 0 的股數。",
                ref blockingError,
                isBlocking: true);
        }

        var unitPrice = ParseDecimal(
            rowNumber,
            FieldNames.UnitPrice,
            rawUnitPrice,
            diagnostics,
            ref blockingError,
            required: true,
            allowNegative: false);

        if (unitPrice is < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.UnitPrice,
                rawUnitPrice,
                ErrorCodeValueOutOfRange,
                "每股價格不可為負數",
                "請提供大於或等於 0 的每股價格。",
                ref blockingError,
                isBlocking: true);
        }

        var fees = ParseDecimal(
            rowNumber,
            FieldNames.Fees,
            rawFees,
            diagnostics,
            ref blockingError,
            required: false,
            allowNegative: false) ?? 0m;

        if (fees < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                FieldNames.Fees,
                rawFees,
                ErrorCodeValueOutOfRange,
                "手續費不可為負數",
                "請提供大於或等於 0 的手續費。",
                ref blockingError,
                isBlocking: true);
            fees = 0m;
        }

        var currency = ParseCurrency(
            rowNumber,
            rawCurrency,
            diagnostics,
            ref blockingError,
            isBlockingOnUnknown: true);

        decimal? netSettlement = null;
        if (tradeSide is not null && quantity.HasValue && unitPrice.HasValue)
        {
            var gross = quantity.Value * unitPrice.Value;
            netSettlement = tradeSide == TradeSideBuy
                ? -(gross + fees)
                : gross - fees;
        }

        return new StockImportParsedRow(
            RowNumber: rowNumber,
            TradeDate: tradeDate,
            RawSecurityName: null,
            Ticker: ticker,
            TradeSide: tradeSide ?? TradeSideAmbiguous,
            Quantity: quantity,
            UnitPrice: unitPrice,
            Fees: fees,
            Taxes: 0m,
            NetSettlement: netSettlement,
            Currency: currency,
            IsInvalid: blockingError,
            ActionsRequired: []);
    }

    private static bool TryBuildBrokerColumnMapping(
        CsvRow header,
        out BrokerColumnMapping mapping,
        out List<StockImportDiagnosticDto> diagnostics)
    {
        diagnostics = [];
        mapping = default!;

        var rawSecurityNameIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.RawSecurityName]);
        var tickerIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.Ticker]);
        var tradeDateIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.TradeDate]);
        var quantityIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.Quantity]);
        var netSettlementIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.NetSettlement]);
        var unitPriceIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.UnitPrice]);
        var feesIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.Fees]);
        var taxesIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.Taxes]);
        var currencyIndex = FindHeaderIndex(header.Columns, BrokerHeaderAliases[FieldNames.Currency]);

        // 只要有 securityName 或 ticker 其一即可。
        if (!rawSecurityNameIndex.HasValue && !tickerIndex.HasValue)
        {
            diagnostics.Add(CreateDiagnostic(
                rowNumber: 1,
                fieldName: FieldNames.RawSecurityName,
                invalidValue: string.Join(",", header.Columns),
                errorCode: ErrorCodeCsvHeaderMissing,
                message: $"CSV 缺少必要欄位：{FieldNames.RawSecurityName}/{FieldNames.Ticker}",
                correctionGuidance: "請在表頭加入「股名」或「ticker」。"));
        }

        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.TradeDate, tradeDateIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.Quantity, quantityIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.NetSettlement, netSettlementIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.UnitPrice, unitPriceIndex);

        if (diagnostics.Count > 0)
        {
            return false;
        }

        mapping = new BrokerColumnMapping(
            RawSecurityNameIndex: rawSecurityNameIndex,
            TickerIndex: tickerIndex,
            TradeDateIndex: tradeDateIndex!.Value,
            QuantityIndex: quantityIndex!.Value,
            NetSettlementIndex: netSettlementIndex!.Value,
            UnitPriceIndex: unitPriceIndex!.Value,
            FeesIndex: feesIndex,
            TaxesIndex: taxesIndex,
            CurrencyIndex: currencyIndex);

        return true;
    }

    private static bool TryBuildLegacyColumnMapping(
        CsvRow header,
        out LegacyColumnMapping mapping,
        out List<StockImportDiagnosticDto> diagnostics)
    {
        diagnostics = [];
        mapping = default!;

        var tradeDateIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.TradeDate]);
        var tickerIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.Ticker]);
        var tradeTypeIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.TradeType]);
        var quantityIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.Quantity]);
        var unitPriceIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.UnitPrice]);
        var feesIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.Fees]);
        var currencyIndex = FindHeaderIndex(header.Columns, LegacyHeaderAliases[FieldNames.Currency]);

        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.TradeDate, tradeDateIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.Ticker, tickerIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.TradeType, tradeTypeIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.Quantity, quantityIndex);
        AddMissingHeaderErrorIfNeeded(header, diagnostics, FieldNames.UnitPrice, unitPriceIndex);

        if (diagnostics.Count > 0)
        {
            return false;
        }

        mapping = new LegacyColumnMapping(
            TradeDateIndex: tradeDateIndex!.Value,
            TickerIndex: tickerIndex!.Value,
            TradeTypeIndex: tradeTypeIndex!.Value,
            QuantityIndex: quantityIndex!.Value,
            UnitPriceIndex: unitPriceIndex!.Value,
            FeesIndex: feesIndex,
            CurrencyIndex: currencyIndex);

        return true;
    }

    private static void AddMissingHeaderErrorIfNeeded(
        CsvRow header,
        List<StockImportDiagnosticDto> diagnostics,
        string fieldName,
        int? index)
    {
        if (index.HasValue)
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            rowNumber: 1,
            fieldName: fieldName,
            invalidValue: string.Join(",", header.Columns),
            errorCode: ErrorCodeCsvHeaderMissing,
            message: $"CSV 缺少必要欄位：{fieldName}",
            correctionGuidance: $"請在表頭加入欄位「{fieldName}」。"));
    }

    private static decimal? ParseDecimal(
        int rowNumber,
        string fieldName,
        string rawValue,
        List<StockImportDiagnosticDto> diagnostics,
        ref bool blockingError,
        bool required,
        bool allowNegative,
        bool isBlockingOnParseFailure = true)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (required)
            {
                AddDiagnostic(
                    diagnostics,
                    rowNumber,
                    fieldName,
                    rawValue,
                    ErrorCodeRequiredFieldMissing,
                    $"{fieldName} 為必填欄位",
                    $"請填入有效的 {fieldName}。",
                    ref blockingError,
                    isBlocking: true);
            }

            return null;
        }

        if (!TryParseDecimal(rawValue, out var parsed))
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                fieldName,
                rawValue,
                ErrorCodeInvalidNumberFormat,
                $"{fieldName} 格式錯誤",
                $"請填入有效的數字格式（可含千分位）。",
                ref blockingError,
                isBlocking: isBlockingOnParseFailure);
            return null;
        }

        if (!allowNegative && parsed < 0)
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                fieldName,
                rawValue,
                ErrorCodeValueOutOfRange,
                $"{fieldName} 不可為負數",
                $"請填入大於或等於 0 的 {fieldName}。",
                ref blockingError,
                isBlocking: true);
            return parsed;
        }

        return parsed;
    }

    private static DateTime? ParseDate(
        int rowNumber,
        string fieldName,
        string rawValue,
        List<StockImportDiagnosticDto> diagnostics,
        ref bool blockingError,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (required)
            {
                AddDiagnostic(
                    diagnostics,
                    rowNumber,
                    fieldName,
                    rawValue,
                    ErrorCodeRequiredFieldMissing,
                    $"{fieldName} 為必填欄位",
                    $"請填入有效的 {fieldName}。",
                    ref blockingError,
                    isBlocking: true);
            }

            return null;
        }

        if (!DateTime.TryParseExact(
                rawValue.Trim(),
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDate))
        {
            AddDiagnostic(
                diagnostics,
                rowNumber,
                fieldName,
                rawValue,
                ErrorCodeInvalidDateFormat,
                $"{fieldName} 格式錯誤",
                "請使用 yyyy-MM-dd 或 yyyy/MM/dd 格式。",
                ref blockingError,
                isBlocking: true);
            return null;
        }

        return DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
    }

    private static string? ParseCurrency(
        int rowNumber,
        string rawValue,
        List<StockImportDiagnosticDto> diagnostics,
        ref bool blockingError,
        bool isBlockingOnUnknown)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var normalized = rawValue.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "TWD":
            case "NTD":
            case "台幣":
            case "新台幣":
                return "TWD";
            case "USD":
            case "美元":
                return "USD";
            case "EUR":
            case "歐元":
                return "EUR";
            case "GBP":
            case "英鎊":
                return "GBP";
            default:
                AddDiagnostic(
                    diagnostics,
                    rowNumber,
                    FieldNames.Currency,
                    rawValue,
                    ErrorCodeInvalidEnumValue,
                    "不支援的幣別",
                    "目前僅支援 TWD / USD / EUR / GBP。",
                    ref blockingError,
                    isBlocking: isBlockingOnUnknown);
                return null;
        }
    }

    private static string DeriveTradeSide(decimal? netSettlement)
    {
        if (!netSettlement.HasValue || netSettlement.Value == 0)
        {
            return TradeSideAmbiguous;
        }

        return netSettlement.Value < 0 ? TradeSideBuy : TradeSideSell;
    }

    private static string? ParseLegacyTradeSide(string rawTradeType)
    {
        if (string.IsNullOrWhiteSpace(rawTradeType))
        {
            return null;
        }

        var normalized = rawTradeType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "buy" or "買" or "買進" or "1" => TradeSideBuy,
            "sell" or "賣" or "賣出" or "2" => TradeSideSell,
            _ => null
        };
    }

    private static void AddDiagnostic(
        ICollection<StockImportDiagnosticDto> diagnostics,
        int rowNumber,
        string fieldName,
        string? invalidValue,
        string errorCode,
        string message,
        string correctionGuidance,
        ref bool blockingError,
        bool isBlocking)
    {
        diagnostics.Add(CreateDiagnostic(rowNumber, fieldName, invalidValue, errorCode, message, correctionGuidance));

        if (isBlocking)
        {
            blockingError = true;
        }
    }

    private static StockImportDiagnosticDto CreateDiagnostic(
        int rowNumber,
        string fieldName,
        string? invalidValue,
        string errorCode,
        string message,
        string correctionGuidance)
        => new()
        {
            RowNumber = rowNumber,
            FieldName = fieldName,
            InvalidValue = string.IsNullOrWhiteSpace(invalidValue) ? null : invalidValue,
            ErrorCode = errorCode,
            Message = message,
            CorrectionGuidance = correctionGuidance
        };

    private static string DetectFormat(IReadOnlyList<string> headers)
    {
        var brokerScore = 0;
        if (FindHeaderIndex(headers, BrokerHeaderAliases[FieldNames.RawSecurityName]).HasValue) brokerScore++;
        if (FindHeaderIndex(headers, BrokerHeaderAliases[FieldNames.TradeDate]).HasValue) brokerScore++;
        if (FindHeaderIndex(headers, BrokerHeaderAliases[FieldNames.Quantity]).HasValue) brokerScore++;
        if (FindHeaderIndex(headers, BrokerHeaderAliases[FieldNames.NetSettlement]).HasValue) brokerScore++;
        if (FindHeaderIndex(headers, BrokerHeaderAliases[FieldNames.UnitPrice]).HasValue) brokerScore++;

        var legacyScore = 0;
        if (FindHeaderIndex(headers, LegacyHeaderAliases[FieldNames.TradeDate]).HasValue) legacyScore++;
        if (FindHeaderIndex(headers, LegacyHeaderAliases[FieldNames.Ticker]).HasValue) legacyScore++;
        if (FindHeaderIndex(headers, LegacyHeaderAliases[FieldNames.TradeType]).HasValue) legacyScore++;
        if (FindHeaderIndex(headers, LegacyHeaderAliases[FieldNames.Quantity]).HasValue) legacyScore++;
        if (FindHeaderIndex(headers, LegacyHeaderAliases[FieldNames.UnitPrice]).HasValue) legacyScore++;

        if (brokerScore >= 4 && brokerScore > legacyScore)
        {
            return FormatBrokerStatement;
        }

        if (legacyScore >= 4 && legacyScore > brokerScore)
        {
            return FormatLegacyCsv;
        }

        return FormatUnknown;
    }

    private static int? FindHeaderIndex(IReadOnlyList<string> headers, IReadOnlyList<string> aliases)
    {
        var aliasSet = aliases
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < headers.Count; index++)
        {
            var normalizedHeader = NormalizeHeader(headers[index]);
            if (aliasSet.Contains(normalizedHeader))
            {
                return index;
            }
        }

        return null;
    }

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .TrimStart('\uFEFF')
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string GetColumnValue(IReadOnlyList<string> columns, int? index)
    {
        if (!index.HasValue || index.Value < 0 || index.Value >= columns.Count)
        {
            return string.Empty;
        }

        return columns[index.Value].Trim();
    }

    private static string NormalizeSelectedFormat(string selectedFormat)
    {
        if (string.Equals(selectedFormat, FormatLegacyCsv, StringComparison.OrdinalIgnoreCase))
        {
            return FormatLegacyCsv;
        }

        if (string.Equals(selectedFormat, FormatBrokerStatement, StringComparison.OrdinalIgnoreCase))
        {
            return FormatBrokerStatement;
        }

        throw new ArgumentException("SelectedFormat must be either 'legacy_csv' or 'broker_statement'.", nameof(selectedFormat));
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        var normalized = value
            .Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal);

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static string? NormalizeSecurityName(string? securityName)
    {
        if (string.IsNullOrWhiteSpace(securityName))
        {
            return null;
        }

        var replaced = securityName
            .Trim()
            .Replace('\u3000', ' ')
            .Replace('\u00A0', ' ');

        return string.Join(' ', replaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeTicker(string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        return ticker.Trim().ToUpperInvariant();
    }

    private static bool IsBlankRow(CsvRow row)
        => row.Columns.All(string.IsNullOrWhiteSpace);

    private static List<CsvRow> ParseCsvRows(string csvContent)
    {
        List<CsvRow> rows = [];
        List<string> currentRow = [];
        var currentValue = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;

        for (var i = 0; i < csvContent.Length; i++)
        {
            var current = csvContent[i];

            if (current == '"')
            {
                if (inQuotes && i + 1 < csvContent.Length && csvContent[i + 1] == '"')
                {
                    currentValue.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                currentRow.Add(currentValue.ToString());
                currentValue.Clear();
            }
            else if ((current == '\n' || current == '\r') && !inQuotes)
            {
                if (current == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                {
                    i++;
                }

                currentRow.Add(currentValue.ToString());
                rows.Add(new CsvRow(rowNumber, currentRow));

                currentRow = [];
                currentValue.Clear();
                rowNumber++;
            }
            else
            {
                currentValue.Append(current);
            }
        }

        if (currentValue.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentValue.ToString());
            rows.Add(new CsvRow(rowNumber, currentRow));
        }

        return rows;
    }

    private static class FieldNames
    {
        internal const string File = "file";
        internal const string SelectedFormat = "selectedFormat";
        internal const string TradeDate = "tradeDate";
        internal const string RawSecurityName = "rawSecurityName";
        internal const string Ticker = "ticker";
        internal const string TradeType = "tradeType";
        internal const string TradeSide = "tradeSide";
        internal const string Quantity = "quantity";
        internal const string UnitPrice = "unitPrice";
        internal const string Fees = "fees";
        internal const string Taxes = "taxes";
        internal const string NetSettlement = "netSettlement";
        internal const string Currency = "currency";
    }

    private sealed record CsvRow(int RowNumber, IReadOnlyList<string> Columns);

    private sealed record BrokerColumnMapping(
        int? RawSecurityNameIndex,
        int? TickerIndex,
        int TradeDateIndex,
        int QuantityIndex,
        int NetSettlementIndex,
        int UnitPriceIndex,
        int? FeesIndex,
        int? TaxesIndex,
        int? CurrencyIndex);

    private sealed record LegacyColumnMapping(
        int TradeDateIndex,
        int TickerIndex,
        int TradeTypeIndex,
        int QuantityIndex,
        int UnitPriceIndex,
        int? FeesIndex,
        int? CurrencyIndex);
}

public sealed record StockImportParseResult(
    string DetectedFormat,
    string SelectedFormat,
    IReadOnlyList<StockImportParsedRow> Rows,
    IReadOnlyList<StockImportDiagnosticDto> Diagnostics);

public sealed record StockImportParsedRow(
    int RowNumber,
    DateTime? TradeDate,
    string? RawSecurityName,
    string? Ticker,
    string TradeSide,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal Fees,
    decimal Taxes,
    decimal? NetSettlement,
    string? Currency,
    bool IsInvalid,
    IReadOnlyList<string> ActionsRequired);
