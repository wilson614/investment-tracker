/**
 * StockImportButton
 *
 * 股票交易 CSV 匯入按鈕：
 * - legacy csv：保留既有逐列 parse + transactionApi.create 流程
 * - broker / unified：改走 previewImport / executeImport 與 unresolved-row remediation
 */
import { useMemo, useRef, useState } from 'react';
import { Upload } from 'lucide-react';
import {
  CSVImportModal,
  type CSVImportBaselineOpeningPositionInput,
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
import {
  StockMarket,
  Currency,
  CurrencyTransactionType as CurrencyTransactionTypeEnum,
  CurrencyTransactionTypeLabels,
} from '../../types';
import type {
  CreateStockTransactionRequest,
  TransactionType,
  StockMarket as StockMarketType,
  Currency as CurrencyType,
  StockImportBalanceDecisionContext,
  StockImportBaselineRequest,
  StockImportDefaultBalanceAction,
  StockImportExecuteBalanceAction,
  StockImportExecuteRequest,
  StockImportPreviewRequest,
  StockImportPreviewResponse,
  StockImportPreviewRow,
  StockImportDiagnostic,
  StockImportSelectedFormat,
  StockImportSelectedSellBeforeBuyAction,
  StockImportSellBeforeBuyAction,
  StockImportTopUpTransactionType,
  StockImportTradeSide,
  StockImportExecuteRowRequest,
} from '../../types';

interface StockImportButtonProps {
  /** 目標 portfolio ID */
  portfolioId: string;
  /** 綁定帳本幣別（source of truth for TWD-specific UX） */
  boundLedgerCurrencyCode?: string | null;
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
  { value: 'broker_statement', label: '券商' },
  { value: 'legacy_csv', label: '一般' },
];

const topUpTransactionTypeValues: StockImportTopUpTransactionType[] = [
  'Deposit',
  'InitialBalance',
  'Interest',
  'OtherIncome',
];

const topUpTransactionTypeOptions: Array<{ value: StockImportTopUpTransactionType; label: string }> = [
  {
    value: 'Deposit',
    label: CurrencyTransactionTypeLabels[CurrencyTransactionTypeEnum.Deposit],
  },
  {
    value: 'InitialBalance',
    label: CurrencyTransactionTypeLabels[CurrencyTransactionTypeEnum.InitialBalance],
  },
  {
    value: 'Interest',
    label: CurrencyTransactionTypeLabels[CurrencyTransactionTypeEnum.Interest],
  },
  {
    value: 'OtherIncome',
    label: CurrencyTransactionTypeLabels[CurrencyTransactionTypeEnum.OtherIncome],
  },
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

function formatImportDiagnosticMessage(error: StockImportDiagnostic): string {
  const escapedErrorCode = error.errorCode
    ? error.errorCode.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    : '';
  const messageWithoutCodePrefix = escapedErrorCode
    ? error.message.replace(new RegExp(`^${escapedErrorCode}\\s*:\\s*`, 'i'), '').trim()
    : error.message.trim();

  const parts = [messageWithoutCodePrefix || error.message];

  if (error.correctionGuidance) {
    parts.push(`建議：${error.correctionGuidance}`);
  }

  if (error.errorCode) {
    parts.push(`代碼：${error.errorCode}`);
  }

  if (error.invalidValue && error.invalidValue.trim() !== '') {
    parts.push(`輸入值：${error.invalidValue}`);
  }

  return parts.join('；');
}

function mapPreviewErrorsToParseErrors(preview: StockImportPreviewResponse): ParseError[] {
  return preview.errors.map((error) => ({
    row: error.rowNumber,
    column: error.fieldName,
    message: formatImportDiagnosticMessage(error),
  }));
}

function isTopUpTransactionType(
  value: StockImportTopUpTransactionType | null | undefined,
): value is StockImportTopUpTransactionType {
  if (!value) {
    return false;
  }

  return topUpTransactionTypeValues.includes(value);
}

function hasBalanceDecisionRequirement(
  row: StockImportPreviewRow,
): boolean {
  if (row.actionsRequired.includes('select_balance_action')) {
    return true;
  }

  return Boolean(row.balanceDecision?.shortfall && row.balanceDecision.shortfall > 0);
}

function hasResolvedBalanceAction(
  rowSelection: 'default' | 'Margin' | 'TopUp' | undefined,
  rowContext: StockImportBalanceDecisionContext | null | undefined,
  globalDefault: 'Margin' | 'TopUp' | null,
): boolean {
  if (rowSelection === 'Margin' || rowSelection === 'TopUp') {
    return true;
  }

  if (rowContext?.action === 'Margin' || rowContext?.action === 'TopUp') {
    return true;
  }

  return globalDefault === 'Margin' || globalDefault === 'TopUp';
}

function deriveRowBalanceAction(
  rowSelection: 'default' | 'Margin' | 'TopUp' | undefined,
  rowContext: StockImportBalanceDecisionContext | null | undefined,
  globalDefault: 'Margin' | 'TopUp' | null,
): StockImportExecuteBalanceAction {
  if (rowSelection === 'Margin' || rowSelection === 'TopUp') {
    return rowSelection;
  }

  if (rowContext?.action === 'Margin' || rowContext?.action === 'TopUp') {
    return rowContext.action;
  }

  return globalDefault ?? 'None';
}

function deriveTopUpTransactionType(
  rowSelection: 'default' | 'Margin' | 'TopUp' | undefined,
  rowContext: StockImportBalanceDecisionContext | null | undefined,
  rowTopUp: StockImportTopUpTransactionType | null | undefined,
  globalDefault: 'Margin' | 'TopUp' | null,
  globalTopUp: StockImportTopUpTransactionType | null,
): StockImportTopUpTransactionType | undefined {
  const rowContextTopUp = rowContext?.topUpTransactionType;

  if (rowSelection === 'TopUp') {
    if (isTopUpTransactionType(rowTopUp)) {
      return rowTopUp;
    }

    if (isTopUpTransactionType(rowContextTopUp)) {
      return rowContextTopUp;
    }

    if (isTopUpTransactionType(globalTopUp)) {
      return globalTopUp;
    }

    return undefined;
  }

  if (rowSelection === 'Margin') {
    return undefined;
  }

  if (rowContext?.action === 'TopUp') {
    if (isTopUpTransactionType(rowTopUp)) {
      return rowTopUp;
    }

    if (isTopUpTransactionType(rowContextTopUp)) {
      return rowContextTopUp;
    }

    if (isTopUpTransactionType(globalTopUp)) {
      return globalTopUp;
    }

    return undefined;
  }

  if (globalTopUp && globalDefault === 'TopUp') {
    return globalTopUp;
  }

  return undefined;
}

function parseOptionalNumber(value: string): number | undefined {
  const trimmed = value.trim();
  if (!trimmed) {
    return undefined;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function serializeCsvData(csvData: ParsedCSV): string {
  return [
    csvData.headers.join(','),
    ...csvData.rows.map((row) => row.map((cell) => {
      if (/[",\n\r]/.test(cell)) {
        return `"${cell.replace(/"/g, '""')}"`;
      }

      return cell;
    }).join(',')),
  ].join('\n');
}

function buildPreviewBaselineSignature(baseline: StockImportBaselineRequest | undefined): string {
  return JSON.stringify(baseline ?? null);
}

function hasBaselineInput(
  baselineDate: string,
  openingCashBalance: string,
  openingLedgerBalance: string,
  openingPositions: CSVImportBaselineOpeningPositionInput[],
): boolean {
  if (baselineDate.trim() !== '') {
    return true;
  }

  if (openingCashBalance.trim() !== '' || openingLedgerBalance.trim() !== '') {
    return true;
  }

  return openingPositions.some((position) => (
    position.ticker.trim() !== ''
    || position.quantity.trim() !== ''
    || position.totalCost.trim() !== ''
  ));
}

function buildBaselineRequest(
  baselineDate: string,
  openingCashBalance: string,
  openingLedgerBalance: string,
  openingPositions: CSVImportBaselineOpeningPositionInput[],
): StockImportBaselineRequest | undefined {
  if (!hasBaselineInput(baselineDate, openingCashBalance, openingLedgerBalance, openingPositions)) {
    return undefined;
  }

  const normalizedPositions = openingPositions
    .map((position) => {
      const ticker = normalizeTickerValue(position.ticker);
      const quantity = parseOptionalNumber(position.quantity);
      const totalCost = parseOptionalNumber(position.totalCost);

      if (!ticker && quantity === undefined && totalCost === undefined) {
        return null;
      }

      return {
        ...(ticker ? { ticker } : {}),
        ...(quantity !== undefined ? { quantity } : {}),
        ...(totalCost !== undefined ? { totalCost } : {}),
      };
    })
    .filter((position): position is NonNullable<typeof position> => position !== null);

  const parsedOpeningCashBalance = parseOptionalNumber(openingCashBalance);
  const parsedOpeningLedgerBalance = parseOptionalNumber(openingLedgerBalance);

  const request: StockImportBaselineRequest = {
    ...(baselineDate.trim() ? { baselineDate: baselineDate.trim() } : {}),
    ...(parsedOpeningCashBalance !== undefined
      ? { openingCashBalance: parsedOpeningCashBalance }
      : {}),
    ...(parsedOpeningLedgerBalance !== undefined
      ? { openingLedgerBalance: parsedOpeningLedgerBalance }
      : {}),
    ...(normalizedPositions.length > 0 ? { openingPositions: normalizedPositions } : {}),
  };

  if (
    !request.baselineDate
    && request.openingCashBalance === undefined
    && request.openingLedgerBalance === undefined
    && (!request.openingPositions || request.openingPositions.length === 0)
  ) {
    return undefined;
  }

  return request;
}

function requiresSellBeforeBuyHandling(row: StockImportPreviewRow): boolean {
  return row.actionsRequired.includes('choose_sell_before_buy_handling');
}

function deriveRowSellBeforeBuyAction(
  rowSelection: 'default' | 'UseOpeningPosition' | 'CreateAdjustment' | undefined,
  globalDefault: 'UseOpeningPosition' | 'CreateAdjustment' | null,
): StockImportSellBeforeBuyAction {
  if (rowSelection === 'UseOpeningPosition' || rowSelection === 'CreateAdjustment') {
    return rowSelection;
  }

  return globalDefault ?? 'None';
}

function hasResolvedSellBeforeBuyAction(
  rowSelection: 'default' | 'UseOpeningPosition' | 'CreateAdjustment' | undefined,
  globalDefault: 'UseOpeningPosition' | 'CreateAdjustment' | null,
): boolean {
  return deriveRowSellBeforeBuyAction(rowSelection, globalDefault) !== 'None';
}

function toSellBeforeBuyBaselineAction(
  action: 'UseOpeningPosition' | 'CreateAdjustment' | null,
): StockImportSelectedSellBeforeBuyAction | undefined {
  if (action === 'UseOpeningPosition' || action === 'CreateAdjustment') {
    return action;
  }

  return undefined;
}

export function StockImportButton({
  portfolioId,
  boundLedgerCurrencyCode,
  onImportComplete,
  onImportSuccess,
  compact = false,
  renderTrigger,
}: StockImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  const [selectedFormat, setSelectedFormat] = useState<StockImportSelectedFormat>('broker_statement');
  const [detectedFormat, setDetectedFormat] = useState<'legacy_csv' | 'broker_statement' | 'unknown'>('unknown');

  const [isPreviewing, setIsPreviewing] = useState(false);
  const [latestPreview, setLatestPreview] = useState<StockImportPreviewResponse | null>(null);
  const [previewErrors, setPreviewErrors] = useState<ParseError[]>([]);
  const [lastLocalDetectedFormat, setLastLocalDetectedFormat] = useState<'legacy_csv' | 'broker_statement' | 'unknown'>('unknown');

  const [manualTickerByRow, setManualTickerByRow] = useState<Record<number, string>>({});
  const [confirmedTradeSideByRow, setConfirmedTradeSideByRow] = useState<Record<number, StockImportTradeSide>>({});
  const [globalSellBeforeBuyAction, setGlobalSellBeforeBuyAction] = useState<'UseOpeningPosition' | 'CreateAdjustment' | null>(null);
  const [rowSellBeforeBuyActionSelectionByRow, setRowSellBeforeBuyActionSelectionByRow] = useState<Record<number, 'default' | 'UseOpeningPosition' | 'CreateAdjustment'>>({});
  const [globalBalanceAction, setGlobalBalanceAction] = useState<'Margin' | 'TopUp' | null>(null);
  const [globalTopUpTransactionType, setGlobalTopUpTransactionType] = useState<StockImportTopUpTransactionType | null>(null);
  const [rowBalanceActionSelectionByRow, setRowBalanceActionSelectionByRow] = useState<Record<number, 'default' | 'Margin' | 'TopUp'>>({});
  const [rowTopUpTransactionTypeByRow, setRowTopUpTransactionTypeByRow] = useState<Record<number, StockImportTopUpTransactionType | null>>({});

  const [baselineDate, setBaselineDate] = useState('');
  const [openingCashBalanceInput, setOpeningCashBalanceInput] = useState('');
  const [openingLedgerBalanceInput, setOpeningLedgerBalanceInput] = useState('');
  const [openingPositionsInput, setOpeningPositionsInput] = useState<CSVImportBaselineOpeningPositionInput[]>([]);
  const [openingPositionSeed, setOpeningPositionSeed] = useState(1);

  const lastPreviewInputSignatureRef = useRef<string | null>(null);

  const isTwdPortfolio = useMemo(() => {
    const normalized = boundLedgerCurrencyCode?.trim().toUpperCase();
    return normalized === 'TWD';
  }, [boundLedgerCurrencyCode]);

  /**
   * 開啟匯入 modal。
   */
  const handleOpenImport = () => {
    setIsModalOpen(true);
  };

  const clearPreviewSessionState = () => {
    setLatestPreview(null);
    setPreviewErrors([]);
    setManualTickerByRow({});
    setConfirmedTradeSideByRow({});
    setGlobalSellBeforeBuyAction(null);
    setRowSellBeforeBuyActionSelectionByRow({});
    setGlobalBalanceAction(null);
    setGlobalTopUpTransactionType(null);
    setRowBalanceActionSelectionByRow({});
    setRowTopUpTransactionTypeByRow({});
    lastPreviewInputSignatureRef.current = null;
  };

  const resetBaselineState = () => {
    setBaselineDate('');
    setOpeningCashBalanceInput('');
    setOpeningLedgerBalanceInput('');
    setOpeningPositionsInput([]);
    setOpeningPositionSeed(1);
  };

  const resetImportState = () => {
    setSelectedFormat('broker_statement');
    setDetectedFormat('unknown');
    setLastLocalDetectedFormat('unknown');
    clearPreviewSessionState();
    resetBaselineState();
    setIsPreviewing(false);
  };

  const handleRequestPreview = async (csvData: ParsedCSV): Promise<void> => {
    const nextCsvContent = serializeCsvData(csvData);

    const detected = detectStockImportFormat(csvData.headers);
    setDetectedFormat(detected);
    setLastLocalDetectedFormat(detected);

    const baseline = buildBaselineRequest(
      baselineDate,
      openingCashBalanceInput,
      openingLedgerBalanceInput,
      openingPositionsInput,
    );

    const nextPreviewInputSignature = [
      selectedFormat,
      nextCsvContent,
      buildPreviewBaselineSignature(baseline),
    ].join('|');

    const shouldReuseRemediationState =
      latestPreview !== null
      && lastPreviewInputSignatureRef.current === nextPreviewInputSignature;

    const request: StockImportPreviewRequest = {
      portfolioId,
      csvContent: nextCsvContent,
      selectedFormat,
      ...(baseline ? { baseline } : {}),
    };

    setIsPreviewing(true);
    try {
      const preview = await transactionApi.previewImport(request);

      setLatestPreview(preview);
      setSelectedFormat(preview.selectedFormat);
      setDetectedFormat(preview.detectedFormat);
      setPreviewErrors(mapPreviewErrorsToParseErrors(preview));
      lastPreviewInputSignatureRef.current = nextPreviewInputSignature;

      // Build/refresh remediation state. Keep previous manual edits only when preview inputs are unchanged.
      const nextManualTickerByRow: Record<number, string> = {};
      const nextConfirmedTradeSideByRow: Record<number, StockImportTradeSide> = {};
      const nextRowSellBeforeBuyActionSelectionByRow: Record<number, 'default' | 'UseOpeningPosition' | 'CreateAdjustment'> = {};
      const nextRowBalanceActionSelectionByRow: Record<number, 'default' | 'Margin' | 'TopUp'> = {};
      const nextRowTopUpTransactionTypeByRow: Record<number, StockImportTopUpTransactionType | null> = {};

      for (const row of preview.rows) {
        const normalizedTicker = normalizeTickerValue(row.ticker);
        const existingManual = shouldReuseRemediationState
          ? normalizeTickerValue(manualTickerByRow[row.rowNumber])
          : null;

        if (existingManual) {
          nextManualTickerByRow[row.rowNumber] = existingManual;
        } else if (normalizedTicker) {
          nextManualTickerByRow[row.rowNumber] = normalizedTicker;
        }

        const existingSide = shouldReuseRemediationState
          ? confirmedTradeSideByRow[row.rowNumber]
          : undefined;
        if (existingSide) {
          nextConfirmedTradeSideByRow[row.rowNumber] = existingSide;
        } else {
          const parsed = parseTradeSideFromRow(row);
          if (parsed) {
            nextConfirmedTradeSideByRow[row.rowNumber] = parsed;
          }
        }

        const existingSellBeforeBuySelection = shouldReuseRemediationState
          ? rowSellBeforeBuyActionSelectionByRow[row.rowNumber]
          : undefined;
        if (existingSellBeforeBuySelection) {
          nextRowSellBeforeBuyActionSelectionByRow[row.rowNumber] = existingSellBeforeBuySelection;
        } else {
          nextRowSellBeforeBuyActionSelectionByRow[row.rowNumber] = 'default';
        }

        const existingRowSelection = shouldReuseRemediationState
          ? rowBalanceActionSelectionByRow[row.rowNumber]
          : undefined;
        if (existingRowSelection) {
          nextRowBalanceActionSelectionByRow[row.rowNumber] = existingRowSelection;
        } else {
          nextRowBalanceActionSelectionByRow[row.rowNumber] = 'default';
        }

        const existingRowTopUp = shouldReuseRemediationState
          ? rowTopUpTransactionTypeByRow[row.rowNumber]
          : null;
        if (isTopUpTransactionType(existingRowTopUp)) {
          nextRowTopUpTransactionTypeByRow[row.rowNumber] = existingRowTopUp;
        } else if (isTopUpTransactionType(row.balanceDecision?.topUpTransactionType)) {
          nextRowTopUpTransactionTypeByRow[row.rowNumber] = row.balanceDecision.topUpTransactionType;
        } else {
          nextRowTopUpTransactionTypeByRow[row.rowNumber] = null;
        }
      }

      setManualTickerByRow(nextManualTickerByRow);
      setConfirmedTradeSideByRow(nextConfirmedTradeSideByRow);
      setRowSellBeforeBuyActionSelectionByRow(nextRowSellBeforeBuyActionSelectionByRow);
      setRowBalanceActionSelectionByRow(nextRowBalanceActionSelectionByRow);
      setRowTopUpTransactionTypeByRow(nextRowTopUpTransactionTypeByRow);
    } finally {
      setIsPreviewing(false);
    }
  };

  const handleAddOpeningPosition = () => {
    const nextId = `opening-position-${openingPositionSeed}`;
    setOpeningPositionSeed((prev) => prev + 1);
    clearPreviewSessionState();
    setOpeningPositionsInput((prev) => [
      ...prev,
      {
        id: nextId,
        ticker: '',
        quantity: '',
        totalCost: '',
      },
    ]);
  };

  const handleRemoveOpeningPosition = (id: string) => {
    clearPreviewSessionState();
    setOpeningPositionsInput((prev) => prev.filter((position) => position.id !== id));
  };

  const handleChangeOpeningPosition = (
    id: string,
    field: 'ticker' | 'quantity' | 'totalCost',
    value: string,
  ) => {
    clearPreviewSessionState();
    setOpeningPositionsInput((prev) => prev.map((position) => (
      position.id === id
        ? {
            ...position,
            [field]: value,
          }
        : position
    )));
  };

  const shouldHideMappingSelectors = useMemo(() => {
    if (selectedFormat !== 'broker_statement') {
      return false;
    }

    return lastLocalDetectedFormat === 'broker_statement' || detectedFormat === 'broker_statement';
  }, [selectedFormat, lastLocalDetectedFormat, detectedFormat]);

  const detectedFormatLabel = useMemo(() => {
    if (detectedFormat === 'broker_statement' || (detectedFormat === 'unknown' && lastLocalDetectedFormat === 'broker_statement')) {
      return '系統偵測：券商';
    }

    if (detectedFormat === 'legacy_csv' || (detectedFormat === 'unknown' && lastLocalDetectedFormat === 'legacy_csv')) {
      return '系統偵測：一般';
    }

    return '系統偵測：未知格式';
  }, [detectedFormat, lastLocalDetectedFormat]);

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

  const hasUndecidedSellBeforeBuyRows = useMemo(() => {
    if (!latestPreview) return false;

    return latestPreview.rows.some((row) => {
      if (!requiresSellBeforeBuyHandling(row)) {
        return false;
      }

      const rowSelection = rowSellBeforeBuyActionSelectionByRow[row.rowNumber] ?? 'default';
      return !hasResolvedSellBeforeBuyAction(rowSelection, globalSellBeforeBuyAction);
    });
  }, [latestPreview, rowSellBeforeBuyActionSelectionByRow, globalSellBeforeBuyAction]);

  const hasUndecidedBalanceActionRows = useMemo(() => {
    if (!latestPreview) return false;

    return latestPreview.rows.some((row) => {
      if (!hasBalanceDecisionRequirement(row)) {
        return false;
      }

      const rowSelection = rowBalanceActionSelectionByRow[row.rowNumber] ?? 'default';
      return !hasResolvedBalanceAction(rowSelection, row.balanceDecision, globalBalanceAction);
    });
  }, [latestPreview, rowBalanceActionSelectionByRow, globalBalanceAction]);

  const hasMissingTopUpTransactionType = useMemo(() => {
    if (!latestPreview || isTwdPortfolio) return false;

    return latestPreview.rows.some((row) => {
      if (!hasBalanceDecisionRequirement(row)) {
        return false;
      }

      const rowSelection = rowBalanceActionSelectionByRow[row.rowNumber] ?? 'default';
      const effectiveAction = deriveRowBalanceAction(
        rowSelection,
        row.balanceDecision,
        globalBalanceAction,
      );

      if (effectiveAction !== 'TopUp') {
        return false;
      }

      const effectiveTopUpType = deriveTopUpTransactionType(
        rowSelection,
        row.balanceDecision,
        rowTopUpTransactionTypeByRow[row.rowNumber],
        globalBalanceAction,
        globalTopUpTransactionType,
      );

      return !effectiveTopUpType;
    });
  }, [
    latestPreview,
    isTwdPortfolio,
    rowBalanceActionSelectionByRow,
    rowTopUpTransactionTypeByRow,
    globalBalanceAction,
    globalTopUpTransactionType,
  ]);

  const executeDisabledReason = useMemo(() => {
    if (!latestPreview) {
      return '請先產生預覽';
    }

    if (previewErrors.length > 0) {
      return '預覽有錯誤，請先修正';
    }

    if (latestPreview.rows.length === 0) {
      return '預覽無可匯入資料';
    }

    if (hasUnresolvedTicker) {
      return '仍有列需要手動輸入股票代號';
    }

    if (hasUnconfirmedTradeSide) {
      return '仍有列缺少買賣方向，請先逐列確認';
    }

    if (hasUndecidedSellBeforeBuyRows) {
      return '仍有列需要指定賣先買後處理方式，請先選擇全域預設或逐列設定';
    }

    if (hasUndecidedBalanceActionRows) {
      return '已選擇「逐筆決定」，請先完成所有短缺列的餘額不足處理方式';
    }

    if (hasMissingTopUpTransactionType) {
      return '補足餘額需選擇交易類型';
    }

    return null;
  }, [
    latestPreview,
    previewErrors,
    hasUnresolvedTicker,
    hasUnconfirmedTradeSide,
    hasUndecidedSellBeforeBuyRows,
    hasUndecidedBalanceActionRows,
    hasMissingTopUpTransactionType,
  ]);

  const remediationRows = useMemo<CSVImportRemediationRow[]>(() => {
    if (!latestPreview) return [];

    return latestPreview.rows.map((row) => {
      const requiresTickerInput = row.actionsRequired.includes('input_ticker');
      const requiresTradeSideConfirmation = row.actionsRequired.includes('confirm_trade_side');
      const requiresSellBeforeBuy = requiresSellBeforeBuyHandling(row);
      const requiresBalanceAction = hasBalanceDecisionRequirement(row);

      const manualTicker = normalizeTickerValue(manualTickerByRow[row.rowNumber]);
      const effectiveTicker = normalizeTickerValue(manualTicker ?? row.ticker);
      const side = confirmedTradeSideByRow[row.rowNumber] ?? parseTradeSideFromRow(row);

      const rowSellBeforeBuySelection = rowSellBeforeBuyActionSelectionByRow[row.rowNumber] ?? 'default';
      const effectiveSellBeforeBuyAction = deriveRowSellBeforeBuyAction(
        rowSellBeforeBuySelection,
        globalSellBeforeBuyAction,
      );

      const rowSelection = rowBalanceActionSelectionByRow[row.rowNumber] ?? 'default';
      const rowTopUpType = rowTopUpTransactionTypeByRow[row.rowNumber] ?? null;
      const effectiveBalanceAction = deriveRowBalanceAction(
        rowSelection,
        row.balanceDecision,
        globalBalanceAction,
      );
      const effectiveTopUpTransactionType = deriveTopUpTransactionType(
        rowSelection,
        row.balanceDecision,
        rowTopUpType,
        globalBalanceAction,
        globalTopUpTransactionType,
      );

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
        requiresSellBeforeBuyHandling: requiresSellBeforeBuy,
        sellBeforeBuyActionSelection: rowSellBeforeBuySelection,
        effectiveSellBeforeBuyAction:
          effectiveSellBeforeBuyAction === 'UseOpeningPosition' || effectiveSellBeforeBuyAction === 'CreateAdjustment'
            ? effectiveSellBeforeBuyAction
            : null,
        usesPartialHistoryAssumption: row.usesPartialHistoryAssumption,
        requiresBalanceAction,
        balanceActionSelection: rowSelection,
        effectiveBalanceAction:
          effectiveBalanceAction === 'Margin' || effectiveBalanceAction === 'TopUp'
            ? effectiveBalanceAction
            : null,
        topUpTransactionType: effectiveTopUpTransactionType ?? null,
        shortfall: row.balanceDecision?.shortfall ?? null,
        availableBalance: row.balanceDecision?.availableBalance ?? null,
        requiredAmount: row.balanceDecision?.requiredAmount ?? null,
        note: requiresTickerInput
          ? '此列需手動輸入 ticker'
          : requiresTradeSideConfirmation
            ? '此列需手動確認買賣方向'
            : requiresSellBeforeBuy
              ? '此列需指定賣先買後處理方式'
              : requiresBalanceAction
                ? '此列需指定餘額不足處理方式'
                : row.usesPartialHistoryAssumption
                  ? '此列套用節錄匯入假設，必要時可調整賣先買後處理方式'
                  : null,
      };
    });
  }, [
    latestPreview,
    manualTickerByRow,
    confirmedTradeSideByRow,
    rowSellBeforeBuyActionSelectionByRow,
    rowBalanceActionSelectionByRow,
    rowTopUpTransactionTypeByRow,
    globalSellBeforeBuyAction,
    globalBalanceAction,
    globalTopUpTransactionType,
  ]);

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

      if (!ticker) {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '股票代號',
          message: '此列尚未提供股票代號，請先於預覽區補齊',
        });
        continue;
      }

      if (!confirmedTradeSide) {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '買賣方向',
          message: '此列尚未確認買賣方向，請先於預覽區逐列確認',
        });
        continue;
      }

      const rowSellBeforeBuySelection = rowSellBeforeBuyActionSelectionByRow[row.rowNumber] ?? 'default';
      const needsSellBeforeBuyHandling = requiresSellBeforeBuyHandling(row);
      const effectiveSellBeforeBuyAction = deriveRowSellBeforeBuyAction(
        rowSellBeforeBuySelection,
        globalSellBeforeBuyAction,
      );

      const rowBalanceSelection = rowBalanceActionSelectionByRow[row.rowNumber] ?? 'default';
      const rowTopUpType = rowTopUpTransactionTypeByRow[row.rowNumber] ?? null;
      const requiresBalanceAction = hasBalanceDecisionRequirement(row);

      const effectiveBalanceAction = deriveRowBalanceAction(
        rowBalanceSelection,
        row.balanceDecision,
        globalBalanceAction,
      );

      const effectiveTopUpTransactionType = deriveTopUpTransactionType(
        rowBalanceSelection,
        row.balanceDecision,
        rowTopUpType,
        globalBalanceAction,
        globalTopUpTransactionType,
      );

      if (needsSellBeforeBuyHandling && effectiveSellBeforeBuyAction === 'None') {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '賣先買後處理方式',
          message: '此列尚未決定賣先買後處理方式，請先選擇「使用期初持倉」或「建立調整」',
        });
        continue;
      }

      if (requiresBalanceAction && effectiveBalanceAction === 'None') {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '餘額不足處理方式',
          message: '此列尚未決定餘額不足處理方式，請先逐列指定',
        });
        continue;
      }

      if (requiresBalanceAction && effectiveBalanceAction === 'TopUp' && !isTwdPortfolio && !effectiveTopUpTransactionType) {
        unresolvedExecuteErrors.push({
          row: row.rowNumber,
          column: '補足交易類型',
          message: '選擇 Top-up 時需指定交易類型',
        });
        continue;
      }

      rows.push({
        rowNumber: row.rowNumber,
        ticker,
        confirmedTradeSide,
        exclude: false,
        ...(needsSellBeforeBuyHandling
          && rowSellBeforeBuySelection !== 'default'
          && (effectiveSellBeforeBuyAction === 'UseOpeningPosition' || effectiveSellBeforeBuyAction === 'CreateAdjustment')
          ? { sellBeforeBuyAction: effectiveSellBeforeBuyAction }
          : {}),
        ...(requiresBalanceAction && effectiveBalanceAction !== 'None'
          ? {
              balanceAction: effectiveBalanceAction,
              ...(effectiveBalanceAction === 'TopUp' && effectiveTopUpTransactionType && !isTwdPortfolio
                ? { topUpTransactionType: effectiveTopUpTransactionType }
                : {}),
            }
          : {}),
      });
    }

    if (unresolvedExecuteErrors.length > 0) {
      return {
        success: false,
        errors: unresolvedExecuteErrors,
      };
    }

    const defaultBalanceAction: StockImportDefaultBalanceAction | undefined = globalBalanceAction
      ? {
          action: globalBalanceAction,
          ...(globalBalanceAction === 'TopUp' && !isTwdPortfolio
            ? { topUpTransactionType: globalTopUpTransactionType ?? undefined }
            : {}),
        }
      : undefined;

    const baselineSellBeforeBuyAction = toSellBeforeBuyBaselineAction(globalSellBeforeBuyAction);
    const baselineDecision = baselineSellBeforeBuyAction
      ? {
          sellBeforeBuyAction: baselineSellBeforeBuyAction,
        }
      : undefined;

    const request: StockImportExecuteRequest = {
      sessionId: latestPreview.sessionId,
      portfolioId,
      rows,
      ...(baselineDecision ? { baselineDecision } : {}),
      ...(defaultBalanceAction ? { defaultBalanceAction } : {}),
    };

    const response = await transactionApi.executeImport(request);

    const errors: ParseError[] = response.errors.map((error) => ({
      row: error.rowNumber,
      column: error.fieldName,
      message: formatImportDiagnosticMessage(error),
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
    formatLabel: '匯入類型',
    selectedFormat,
    detectedFormatLabel,
    formatOptions,
    onChangeFormat: (nextValue) => {
      if (nextValue === 'legacy_csv' || nextValue === 'broker_statement') {
        if (nextValue !== selectedFormat) {
          setSelectedFormat(nextValue);
          clearPreviewSessionState();
          resetBaselineState();
        }
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
    baselineDate,
    openingCashBalance: openingCashBalanceInput,
    openingLedgerBalance: openingLedgerBalanceInput,
    openingPositions: openingPositionsInput,
    onChangeBaselineDate: (value) => {
      setBaselineDate(value);
      clearPreviewSessionState();
    },
    onChangeOpeningCashBalance: (value) => {
      setOpeningCashBalanceInput(value);
      clearPreviewSessionState();
    },
    onChangeOpeningLedgerBalance: (value) => {
      setOpeningLedgerBalanceInput(value);
      clearPreviewSessionState();
    },
    onAddOpeningPosition: handleAddOpeningPosition,
    onRemoveOpeningPosition: handleRemoveOpeningPosition,
    onChangeOpeningPosition: handleChangeOpeningPosition,
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
    globalSellBeforeBuyActionLabel: '賣先買後預設處理方式',
    globalSellBeforeBuyActionHint: '遇到先賣後買時，先套用此預設；選「逐筆決定」可在下方逐列調整。',
    globalSellBeforeBuyAction,
    onChangeGlobalSellBeforeBuyAction: (value) => {
      setGlobalSellBeforeBuyAction(value);
    },
    rowSellBeforeBuyActionLabel: '逐列賣先買後處理方式',
    onChangeRowSellBeforeBuyActionSelection: (rowNumber, value) => {
      setRowSellBeforeBuyActionSelectionByRow((prev) => ({
        ...prev,
        [rowNumber]: value,
      }));
    },
    globalBalanceActionLabel: '餘額不足預設處理方式',
    globalBalanceAction,
    onChangeGlobalBalanceAction: (value) => {
      setGlobalBalanceAction(value);
      if (value !== 'TopUp') {
        setGlobalTopUpTransactionType(null);
      }
    },
    globalTopUpTransactionType,
    onChangeGlobalTopUpTransactionType: (value) => {
      setGlobalTopUpTransactionType(value);
    },
    hideTopUpTransactionTypeSelector: isTwdPortfolio,
    topUpTransactionTypeFixedNotice: '台幣投組匯入補足一律使用存入（Deposit）。',
    rowBalanceActionLabel: '逐列餘額不足處理方式',
    rowTopUpTransactionTypeLabel: '逐列補足交易類型',
    onChangeRowBalanceActionSelection: (rowNumber, value) => {
      setRowBalanceActionSelectionByRow((prev) => ({
        ...prev,
        [rowNumber]: value,
      }));

      if (value !== 'TopUp') {
        setRowTopUpTransactionTypeByRow((prev) => ({
          ...prev,
          [rowNumber]: null,
        }));
      }
    },
    onChangeRowTopUpTransactionType: (rowNumber, value) => {
      setRowTopUpTransactionTypeByRow((prev) => ({
        ...prev,
        [rowNumber]: value,
      }));
    },
    topUpTransactionTypeOptions,
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
        onCsvParsed={(csvData) => {
          const detectedFormat = detectStockImportFormat(csvData.headers);
          setLastLocalDetectedFormat(detectedFormat);
          setSelectedFormat('broker_statement');
          clearPreviewSessionState();
          resetBaselineState();
        }}
        hideMappingSelectors={shouldHideMappingSelectors}
        hiddenMappingFieldNames={['market', 'currency', 'exchangeRate']}
        onRequestPreview={(csvData) => handleRequestPreview(csvData)}
        onExecute={handleExecuteImport}
        onImport={handleLegacyImport}
        previewExtensions={previewExtensions}
      />
    </>
  );
}
