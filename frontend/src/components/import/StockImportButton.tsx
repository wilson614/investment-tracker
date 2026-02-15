/**
 * StockImportButton
 *
 * 股票交易 CSV 匯入按鈕：
 * - legacy csv：保留既有逐列 parse + transactionApi.create 流程
 * - broker / unified：改走 previewImport / executeImport 與 unresolved-row remediation
 */
import { useMemo, useState } from 'react';
import { Upload } from 'lucide-react';
import {
  CSVImportModal,
  type CSVImportPreviewExtensions,
  type CSVImportRemediationRow,
  type FieldDefinition,
} from './CSVImportModal';
import { transactionApi } from '../../services/api';
import {
  getRowValue,
  parseDate,
  parseNumber,
  formatDateISO,
  detectStockImportFormat,
  normalizeCurrencyCode,
  normalizeTickerValue,
  BROKER_STATEMENT_FIELD_ALIASES,
  LEGACY_STOCK_FIELD_ALIASES,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';
import { StockMarket, Currency } from '../../types';
import type {
  CreateStockTransactionRequest,
  TransactionType,
  StockMarket as StockMarketType,
  Currency as CurrencyType,
  StockImportExecuteRequest,
  StockImportPreviewRequest,
  StockImportPreviewResponse,
  StockImportPreviewRow,
  StockImportSelectedFormat,
  StockImportTradeSide,
  StockImportExecuteRowRequest,
} from '../../types';

interface StockImportButtonProps {
  /** 目標 portfolio ID */
  portfolioId: string;
  /** 匯入完成後 callback（通常用於重新載入頁面資料） */
  onImportComplete: () => void;
  /** 匯入成功後執行 shared cache invalidation */
  onImportSuccess?: () => void;
  /** 縮小版按鈕樣式 */
  compact?: boolean;
  /** 若提供，改用自訂 trigger（常用於搭配 FileDropdown） */
  renderTrigger?: (onClick: () => void) => React.ReactNode;
}

/**
 * 股票交易 CSV 欄位定義。
 *
 * 匯率欄位為選填：若 CSV 未提供，backend 會自動從交易日期抓取。
 * 台股會自動設為匯率 = 1。
 */
const stockFields: FieldDefinition[] = [
  {
    name: 'date',
    label: '日期',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.date, ...BROKER_STATEMENT_FIELD_ALIASES.tradeDate],
    required: true,
  },
  {
    name: 'ticker',
    label: '股票代號',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.ticker, ...BROKER_STATEMENT_FIELD_ALIASES.ticker],
    required: false,
  },
  {
    name: 'securityName',
    label: '標的名稱（券商）',
    aliases: [...BROKER_STATEMENT_FIELD_ALIASES.securityName],
    required: false,
  },
  {
    name: 'type',
    label: '交易類型',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.type],
    required: false,
  },
  {
    name: 'market',
    label: '市場',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.market],
    required: false,
  },
  {
    name: 'currency',
    label: '幣別',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.currency, ...BROKER_STATEMENT_FIELD_ALIASES.currency],
    required: false,
  },
  {
    name: 'shares',
    label: '股數',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.shares, ...BROKER_STATEMENT_FIELD_ALIASES.quantity],
    required: true,
  },
  {
    name: 'price',
    label: '每股價格',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.price, ...BROKER_STATEMENT_FIELD_ALIASES.unitPrice],
    required: true,
  },
  {
    name: 'netSettlement',
    label: '淨收付（券商）',
    aliases: [...BROKER_STATEMENT_FIELD_ALIASES.netSettlement],
    required: false,
  },
  {
    name: 'fees',
    label: '手續費',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.fees, ...BROKER_STATEMENT_FIELD_ALIASES.fees],
    required: false,
  },
  {
    name: 'taxes',
    label: '交易稅（券商）',
    aliases: [...BROKER_STATEMENT_FIELD_ALIASES.taxes],
    required: false,
  },
  {
    name: 'exchangeRate',
    label: '匯率（選填）',
    aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', '匯率'],
    required: false,
  },
  {
    name: 'notes',
    label: '備註',
    aliases: [...LEGACY_STOCK_FIELD_ALIASES.notes, ...BROKER_STATEMENT_FIELD_ALIASES.notes],
    required: false,
  },
];

const formatOptions: Array<{ value: StockImportSelectedFormat; label: string }> = [
  { value: 'broker_statement', label: '券商對帳單（Broker Statement）' },
  { value: 'legacy_csv', label: '舊版 CSV（Legacy CSV）' },
];

/**
 * 將 CSV 內的交易類型文字轉成 enum。
 *
 * 支援：
 * - 中文：買/賣/分割/調整
 * - 英文：buy/sell/split/adjust
 * - 數字：1-4
 */
function parseTransactionType(typeStr: string): TransactionType | null {
  const normalized = typeStr.toLowerCase().trim();

  // Chinese mappings
  if (normalized.includes('買') || normalized.includes('buy')) {
    return 1 as TransactionType; // Buy
  }
  if (normalized.includes('賣') || normalized.includes('sell')) {
    return 2 as TransactionType; // Sell
  }
  if (normalized.includes('分割') || normalized.includes('split')) {
    return 3 as TransactionType; // Split
  }
  if (normalized.includes('調整') || normalized.includes('adjust')) {
    return 4 as TransactionType; // Adjustment
  }

  // Numeric mappings
  const num = parseInt(normalized);
  if (num >= 1 && num <= 4) {
    return num as TransactionType;
  }

  return null;
}

/**
 * 將 CSV 內的市場文字轉成 enum。
 *
 * 支援：
 * - 英文代碼：TW/US/UK/EU
 * - 中文名稱：台灣/美國/英國/歐洲
 * - 數字：1-4
 */
function parseMarket(marketStr: string): StockMarketType | null {
  const normalized = marketStr.toUpperCase().trim();

  // Direct code mappings
  if (normalized === 'TW' || normalized === '台灣' || normalized === '臺灣') {
    return StockMarket.TW;
  }
  if (normalized === 'US' || normalized === '美國') {
    return StockMarket.US;
  }
  if (normalized === 'UK' || normalized === '英國') {
    return StockMarket.UK;
  }
  if (normalized === 'EU' || normalized === '歐洲') {
    return StockMarket.EU;
  }

  // Numeric mappings
  const num = parseInt(normalized);
  if (num >= 1 && num <= 4) {
    return num as StockMarketType;
  }

  return null;
}

/**
 * 將 CSV 內的幣別文字轉成 enum。
 *
 * 支援：
 * - 英文代碼：TWD/USD/GBP/EUR
 * - 中文名稱：台幣/美元/英鎊/歐元
 * - 數字：1-4
 */
function parseCurrency(currencyStr: string): CurrencyType | null {
  const normalized = currencyStr.toUpperCase().trim();

  // Direct code mappings
  if (normalized === 'TWD' || normalized === '台幣' || normalized === '臺幣' || normalized === 'NTD' || normalized === 'NT$') {
    return Currency.TWD;
  }
  if (normalized === 'USD' || normalized === '美元' || normalized === 'US$' || normalized === '$') {
    return Currency.USD;
  }
  if (normalized === 'GBP' || normalized === '英鎊' || normalized === '£') {
    return Currency.GBP;
  }
  if (normalized === 'EUR' || normalized === '歐元' || normalized === '€') {
    return Currency.EUR;
  }

  // Numeric mappings
  const num = parseInt(normalized);
  if (num >= 1 && num <= 4) {
    return num as CurrencyType;
  }

  return null;
}

function parseTradeSideFromRow(row: StockImportPreviewRow): StockImportTradeSide | null {
  if (row.confirmedTradeSide === 'buy' || row.confirmedTradeSide === 'sell') {
    return row.confirmedTradeSide;
  }

  if (row.tradeSide === 'buy' || row.tradeSide === 'sell') {
    return row.tradeSide;
  }

  return null;
}

function mapPreviewErrorsToParseErrors(preview: StockImportPreviewResponse): ParseError[] {
  return preview.errors.map((error) => ({
    row: error.rowNumber,
    column: error.fieldName,
    message: [error.message, error.correctionGuidance].filter(Boolean).join('；'),
  }));
}

export function StockImportButton({
  portfolioId,
  onImportComplete,
  onImportSuccess,
  compact = false,
  renderTrigger,
}: StockImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  const [csvContent, setCsvContent] = useState('');
  const [selectedFormat, setSelectedFormat] = useState<StockImportSelectedFormat>('broker_statement');
  const [detectedFormat, setDetectedFormat] = useState<'legacy_csv' | 'broker_statement' | 'unknown'>('unknown');

  const [isPreviewing, setIsPreviewing] = useState(false);
  const [latestPreview, setLatestPreview] = useState<StockImportPreviewResponse | null>(null);
  const [previewErrors, setPreviewErrors] = useState<ParseError[]>([]);

  const [manualTickerByRow, setManualTickerByRow] = useState<Record<number, string>>({});
  const [confirmedTradeSideByRow, setConfirmedTradeSideByRow] = useState<Record<number, StockImportTradeSide>>({});

  /**
   * 開啟匯入 modal。
   */
  const handleOpenImport = () => {
    setIsModalOpen(true);
  };

  const resetImportState = () => {
    setCsvContent('');
    setSelectedFormat('broker_statement');
    setDetectedFormat('unknown');
    setLatestPreview(null);
    setPreviewErrors([]);
    setManualTickerByRow({});
    setConfirmedTradeSideByRow({});
    setIsPreviewing(false);
  };

  const handleRequestPreview = async (csvData: ParsedCSV): Promise<void> => {
    if (!csvContent) {
      const reconstructed = [
        csvData.headers.join(','),
        ...csvData.rows.map((row) => row.map((cell) => {
          if (/[",\n\r]/.test(cell)) {
            return `"${cell.replace(/"/g, '""')}"`;
          }
          return cell;
        }).join(',')),
      ].join('\n');
      setCsvContent(reconstructed);
    }

    const nextCsvContent = csvContent || [
      csvData.headers.join(','),
      ...csvData.rows.map((row) => row.map((cell) => {
        if (/[",\n\r]/.test(cell)) {
          return `"${cell.replace(/"/g, '""')}"`;
        }
        return cell;
      }).join(',')),
    ].join('\n');

    const detected = detectStockImportFormat(csvData.headers);
    setDetectedFormat(detected);

    const request: StockImportPreviewRequest = {
      portfolioId,
      csvContent: nextCsvContent,
      selectedFormat,
    };

    setIsPreviewing(true);
    try {
      const preview = await transactionApi.previewImport(request);

      setLatestPreview(preview);
      setSelectedFormat(preview.selectedFormat);
      setPreviewErrors(mapPreviewErrorsToParseErrors(preview));

      // Build/refresh remediation state, keep previous manual edits
      const nextManualTickerByRow: Record<number, string> = {};
      const nextConfirmedTradeSideByRow: Record<number, StockImportTradeSide> = {};

      for (const row of preview.rows) {
        const normalizedTicker = normalizeTickerValue(row.ticker);
        const existingManual = normalizeTickerValue(manualTickerByRow[row.rowNumber]);

        if (existingManual) {
          nextManualTickerByRow[row.rowNumber] = existingManual;
        } else if (normalizedTicker) {
          nextManualTickerByRow[row.rowNumber] = normalizedTicker;
        }

        const existingSide = confirmedTradeSideByRow[row.rowNumber];
        if (existingSide) {
          nextConfirmedTradeSideByRow[row.rowNumber] = existingSide;
        } else {
          const parsed = parseTradeSideFromRow(row);
          if (parsed) {
            nextConfirmedTradeSideByRow[row.rowNumber] = parsed;
          }
        }
      }

      setManualTickerByRow(nextManualTickerByRow);
      setConfirmedTradeSideByRow(nextConfirmedTradeSideByRow);
    } finally {
      setIsPreviewing(false);
    }
  };

  const hasUnresolvedTicker = useMemo(() => {
    if (!latestPreview) return false;

    return latestPreview.rows.some((row) => {
      if (!row.actionsRequired.includes('input_ticker')) {
        return false;
      }

      const resolved = normalizeTickerValue(manualTickerByRow[row.rowNumber] ?? row.ticker);
      return !resolved;
    });
  }, [latestPreview, manualTickerByRow]);

  const hasUnconfirmedTradeSide = useMemo(() => {
    if (!latestPreview) return false;

    return latestPreview.rows.some((row) => {
      if (row.actionsRequired.includes('confirm_trade_side')) {
        return !confirmedTradeSideByRow[row.rowNumber];
      }

      return parseTradeSideFromRow(row) === null;
    });
  }, [latestPreview, confirmedTradeSideByRow]);

  const executeDisabledReason = useMemo(() => {
    if (!latestPreview) {
      return '請先產生預覽';
    }

    if (hasUnresolvedTicker) {
      return '仍有列需要手動輸入股票代號';
    }

    if (hasUnconfirmedTradeSide) {
      return '仍有列缺少買賣方向，請先逐列確認';
    }

    return null;
  }, [latestPreview, hasUnresolvedTicker, hasUnconfirmedTradeSide]);

  const remediationRows = useMemo<CSVImportRemediationRow[]>(() => {
    if (!latestPreview) return [];

    return latestPreview.rows.map((row) => {
      const requiresTickerInput = row.actionsRequired.includes('input_ticker');
      const requiresTradeSideConfirmation = row.actionsRequired.includes('confirm_trade_side');

      const manualTicker = normalizeTickerValue(manualTickerByRow[row.rowNumber]);
      const effectiveTicker = normalizeTickerValue(manualTicker ?? row.ticker);
      const side = confirmedTradeSideByRow[row.rowNumber] ?? parseTradeSideFromRow(row);

      return {
        rowNumber: row.rowNumber,
        rawSecurityName: row.rawSecurityName,
        ticker: row.ticker,
        displayTicker: effectiveTicker,
        status: row.status,
        requiresTickerInput,
        manualTicker: manualTicker ?? row.ticker ?? '',
        tradeSide: row.tradeSide,
        confirmedTradeSide: side,
        requiresTradeSideConfirmation,
        note: requiresTickerInput
          ? '此列需手動輸入 ticker'
          : requiresTradeSideConfirmation
            ? '此列需手動確認買賣方向'
            : null,
      };
    });
  }, [latestPreview, manualTickerByRow, confirmedTradeSideByRow]);

  const handleExecuteImport = async (): Promise<{ success: boolean; errors: ParseError[]; successCount?: number }> => {
    if (!latestPreview) {
      return {
        success: false,
        errors: [{ row: 1, message: '請先產生預覽' }],
      };
    }

    const unresolvedExecuteErrors: ParseError[] = [];
    const rows: StockImportExecuteRowRequest[] = [];

    for (const row of latestPreview.rows) {
      const ticker = normalizeTickerValue(manualTickerByRow[row.rowNumber] ?? row.ticker) ?? '';
      const confirmedTradeSide = confirmedTradeSideByRow[row.rowNumber] ?? parseTradeSideFromRow(row);

      if (!confirmedTradeSide) {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '買賣方向',
          message: '此列尚未確認買賣方向，請先於預覽區逐列確認',
        });
        continue;
      }

      rows.push({
        rowNumber: row.rowNumber,
        ticker,
        confirmedTradeSide,
        exclude: false,
        balanceAction: 'None',
      });
    }

    if (unresolvedExecuteErrors.length > 0) {
      return {
        success: false,
        errors: unresolvedExecuteErrors,
      };
    }

    const request: StockImportExecuteRequest = {
      sessionId: latestPreview.sessionId,
      portfolioId,
      rows,
    };

    const response = await transactionApi.executeImport(request);

    const errors: ParseError[] = response.errors.map((error) => ({
      row: error.rowNumber,
      column: error.fieldName,
      message: [error.message, error.correctionGuidance].filter(Boolean).join('；'),
    }));

    if (response.summary.insertedRows > 0) {
      onImportSuccess?.();
      onImportComplete();
    }

    return {
      success: response.status === 'committed' && errors.length === 0,
      errors,
      successCount: response.summary.insertedRows,
    };
  };

  /**
   * Legacy fallback: 逐列解析/驗證後呼叫 API 建立交易。
   */
  const handleLegacyImport = async (
    csvData: ParsedCSV,
    mapping: ColumnMapping
  ): Promise<{ success: boolean; errors: ParseError[] }> => {
    const errors: ParseError[] = [];
    let successCount = 0;

    // Ensure chronological order to avoid sell-before-buy issues
    const sortedRows = csvData.rows
      .map((row, index) => ({ row, index }))
      .sort((a, b) => {
        const aDateStr = getRowValue(a.row, csvData.headers, mapping, 'date') ?? '';
        const bDateStr = getRowValue(b.row, csvData.headers, mapping, 'date') ?? '';
        const aDate = parseDate(aDateStr)?.getTime();
        const bDate = parseDate(bDateStr)?.getTime();

        if (aDate == null && bDate == null) return a.index - b.index;
        if (aDate == null) return 1;
        if (bDate == null) return -1;
        if (aDate !== bDate) return aDate - bDate;
        return a.index - b.index;
      });

    for (let i = 0; i < sortedRows.length; i++) {
      const { row, index: originalIndex } = sortedRows[i];
      const rowNum = csvData.rowMetadata[originalIndex]?.originalRowNumber ?? originalIndex + 2; // 1-based, skip header row

      try {
        // Parse date
        const dateStr = getRowValue(row, csvData.headers, mapping, 'date');
        if (!dateStr) {
          errors.push({ row: rowNum, column: '日期', message: '日期為必填欄位' });
          continue;
        }
        const parsedDate = parseDate(dateStr);
        if (!parsedDate) {
          errors.push({ row: rowNum, column: '日期', message: `無法解析日期: ${dateStr}` });
          continue;
        }

        // Parse ticker
        const ticker = getRowValue(row, csvData.headers, mapping, 'ticker');
        if (!ticker) {
          errors.push({ row: rowNum, column: '股票代號', message: '股票代號為必填欄位' });
          continue;
        }
        const normalizedTicker = ticker.toUpperCase().trim();

        // Parse type
        const typeStr = getRowValue(row, csvData.headers, mapping, 'type');
        if (!typeStr) {
          errors.push({ row: rowNum, column: '類型', message: '交易類型為必填欄位' });
          continue;
        }
        const transactionType = parseTransactionType(typeStr);
        if (transactionType === null) {
          errors.push({ row: rowNum, column: '類型', message: `無法辨識交易類型: ${typeStr}` });
          continue;
        }

        // Parse market
        const marketStr = getRowValue(row, csvData.headers, mapping, 'market');
        if (!marketStr) {
          errors.push({ row: rowNum, column: '市場', message: '市場為必填欄位' });
          continue;
        }
        const market = parseMarket(marketStr);
        if (market === null) {
          errors.push({ row: rowNum, column: '市場', message: `無法辨識市場: ${marketStr}（支援 TW/US/UK/EU 或 台灣/美國/英國/歐洲）` });
          continue;
        }

        // Parse currency
        const currencyStr = getRowValue(row, csvData.headers, mapping, 'currency');
        if (!currencyStr) {
          errors.push({ row: rowNum, column: '幣別', message: '幣別為必填欄位' });
          continue;
        }
        const normalizedCurrencyStr = normalizeCurrencyCode(currencyStr) ?? currencyStr;
        const currency = parseCurrency(normalizedCurrencyStr);
        if (currency === null) {
          errors.push({ row: rowNum, column: '幣別', message: `無法辨識幣別: ${currencyStr}（支援 TWD/USD/GBP/EUR 或 台幣/美元/英鎊/歐元）` });
          continue;
        }

        // Parse shares
        const sharesStr = getRowValue(row, csvData.headers, mapping, 'shares');
        if (!sharesStr) {
          errors.push({ row: rowNum, column: '股數', message: '股數為必填欄位' });
          continue;
        }
        const shares = parseNumber(sharesStr);
        if (shares === null || shares === 0) {
          errors.push({ row: rowNum, column: '股數', message: `無效的股數: ${sharesStr}` });
          continue;
        }

        // Parse price
        const priceStr = getRowValue(row, csvData.headers, mapping, 'price');
        if (!priceStr) {
          errors.push({ row: rowNum, column: '價格', message: '每股價格為必填欄位' });
          continue;
        }
        const price = parseNumber(priceStr);
        if (price === null || price < 0) {
          errors.push({ row: rowNum, column: '價格', message: `無效的價格: ${priceStr}` });
          continue;
        }

        // Parse exchange rate column for backward-compatible CSV validation only.
        // Exchange rate is now system-calculated and will not be sent in request payload.
        const exchangeRateStr = getRowValue(row, csvData.headers, mapping, 'exchangeRate');
        if (exchangeRateStr) {
          const parsed = parseNumber(exchangeRateStr);
          if (parsed === null || parsed <= 0) {
            errors.push({ row: rowNum, column: '匯率', message: `無效的匯率: ${exchangeRateStr}` });
            continue;
          }
        }

        // Parse optional fields
        const feesStr = getRowValue(row, csvData.headers, mapping, 'fees');
        const notes = getRowValue(row, csvData.headers, mapping, 'notes');

        const fees = feesStr ? Math.abs(parseNumber(feesStr) ?? 0) : 0;

        // Build request
        const request: CreateStockTransactionRequest = {
          portfolioId,
          ticker: normalizedTicker,
          transactionType,
          transactionDate: formatDateISO(parsedDate),
          shares: Math.abs(shares),
          pricePerShare: Math.abs(price),
          fees,
          notes: notes || undefined,
          market,
          currency,
        };

        // Create transaction
        await transactionApi.create(request);
        successCount++;
      } catch (err) {
        errors.push({
          row: rowNum,
          message: err instanceof Error ? err.message : '建立交易失敗',
        });
      }
    }

    if (successCount > 0) {
      onImportSuccess?.();
      onImportComplete();
    }

    return {
      success: errors.length === 0,
      errors,
    };
  };

  const previewExtensions: CSVImportPreviewExtensions = {
    formatLabel: '匯入格式',
    selectedFormat,
    detectedFormatLabel:
      detectedFormat === 'unknown'
        ? '系統偵測：未知格式'
        : detectedFormat === 'broker_statement'
          ? '系統偵測：券商對帳單'
          : '系統偵測：舊版 CSV',
    formatOptions,
    onChangeFormat: (nextValue) => {
      if (nextValue === 'legacy_csv' || nextValue === 'broker_statement') {
        setSelectedFormat(nextValue);
      }
    },
    previewButtonLabel: latestPreview ? '重新預覽' : '產生預覽',
    isPreviewing,
    hasPreview: Boolean(latestPreview),
    summaryItems: latestPreview
      ? [
          { key: 'totalRows', label: '總筆數', value: latestPreview.summary.totalRows },
          { key: 'validRows', label: '可匯入', value: latestPreview.summary.validRows },
          { key: 'requiresActionRows', label: '需補充', value: latestPreview.summary.requiresActionRows },
          { key: 'invalidRows', label: '無效列', value: latestPreview.summary.invalidRows },
        ]
      : [],
    remediationRows,
    previewErrors,
    onManualTickerChange: (rowNumber, value) => {
      setManualTickerByRow((prev) => ({
        ...prev,
        [rowNumber]: normalizeTickerValue(value) ?? '',
      }));
    },
    onChangeTradeSide: (rowNumber, side) => {
      setConfirmedTradeSideByRow((prev) => ({
        ...prev,
        [rowNumber]: side,
      }));
    },
    executeDisabled: Boolean(executeDisabledReason),
    executeDisabledReason,
  };

  return (
    <>
      {renderTrigger ? (
        renderTrigger(handleOpenImport)
      ) : (
        <button
          onClick={handleOpenImport}
          className={compact
            ? "btn-dark flex items-center gap-2 px-3 py-1.5 text-sm"
            : "btn-dark flex items-center gap-2"
          }
        >
          <Upload className={compact ? "w-3.5 h-3.5" : "w-4 h-4"} />
          匯入
        </button>
      )}

      <CSVImportModal
        isOpen={isModalOpen}
        onClose={() => {
          setIsModalOpen(false);
          resetImportState();
        }}
        title="匯入股票交易"
        fields={stockFields}
        onRequestPreview={(csvData) => {
          const content = [
            csvData.headers.join(','),
            ...csvData.rows.map((row) => row.map((cell) => {
              if (/[",\n\r]/.test(cell)) {
                return `"${cell.replace(/"/g, '""')}"`;
              }
              return cell;
            }).join(',')),
          ].join('\n');
          setCsvContent(content);
          return handleRequestPreview(csvData);
        }}
        onExecute={handleExecuteImport}
        onImport={handleLegacyImport}
        previewExtensions={previewExtensions}
      />
    </>
  );
}
