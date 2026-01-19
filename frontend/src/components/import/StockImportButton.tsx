/**
 * StockImportButton
 *
 * 股票交易 CSV 匯入按鈕：封裝「選擇是否使用外幣帳本 → 開啟 CSVImportModal → 逐列解析/驗證 → 呼叫交易 API」流程。
 *
 * 重要行為：
 * - 會先將 CSV rows 依日期排序，避免出現 sell-before-buy 的計算/驗證問題。
 * - 可選擇使用外幣帳本（currency ledger）輔助：非台股時可讓 backend 自動計算匯率。
 */
import { useState, useEffect } from 'react';
import { Upload, Wallet } from 'lucide-react';
import { CSVImportModal, type FieldDefinition } from './CSVImportModal';
import { transactionApi, currencyLedgerApi } from '../../services/api';
import {
  getRowValue,
  parseDate,
  parseNumber,
  formatDateISO,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';
import { FundSource } from '../../types';
import type { CreateStockTransactionRequest, TransactionType, CurrencyLedgerSummary } from '../../types';

interface StockImportButtonProps {
  /** 目標 portfolio ID */
  portfolioId: string;
  /** 匯入完成後 callback（通常用於重新載入頁面資料） */
  onImportComplete: () => void;
  /** 縮小版按鈕樣式 */
  compact?: boolean;
  /** 若提供，改用自訂 trigger（常用於搭配 FileDropdown） */
  renderTrigger?: (onClick: () => void) => React.ReactNode;
}

/**
 * 股票交易 CSV 欄位定義。
 *
 * 設計：當使用外幣帳本（currency ledger）時，非台股可讓 backend 依帳本交易推導匯率，因此匯率欄位會被隱藏。
 */
const getStockFields = (useCurrencyLedger: boolean): FieldDefinition[] => {
  const fields: FieldDefinition[] = [
    {
      name: 'date',
      label: '日期',
      aliases: ['transactionDate', 'transaction_date', 'Date', '交易日期', '日期', '買進日期'],
      required: true,
    },
    {
      name: 'ticker',
      label: '股票代號',
      aliases: ['Ticker', 'symbol', 'Symbol', 'stock', 'Stock', '代碼', '股票', '股票代號'],
      required: true,
    },
    {
      name: 'type',
      label: '交易類型',
      aliases: ['transactionType', 'transaction_type', 'Type', '類型', '買賣'],
      required: true,
    },
    {
      name: 'shares',
      label: '股數',
      aliases: ['Shares', 'quantity', 'Quantity', 'qty', 'Qty', '數量', '股', '買進股數', '股數'],
      required: true,
    },
    {
      name: 'price',
      label: '每股價格',
      aliases: ['pricePerShare', 'price_per_share', 'Price', '價格', '單價', '買進價格'],
      required: true,
    },
  ];

  // Only include exchange rate field when not using currency ledger
  if (!useCurrencyLedger) {
    fields.push({
      name: 'exchangeRate',
      label: '匯率',
      aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', '匯率'],
      required: true,
    });
  }

  fields.push(
    {
      name: 'fees',
      label: '手續費',
      aliases: ['Fees', 'commission', 'Commission', 'fee', 'Fee', '手續費', '費用'],
      required: false,
    },
    {
      name: 'notes',
      label: '備註',
      aliases: ['Notes', 'memo', 'Memo', 'description', 'Description', '備註', '說明'],
      required: false,
    }
  );

  return fields;
};

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

export function StockImportButton({
  portfolioId,
  onImportComplete,
  compact = false,
  renderTrigger,
}: StockImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isSelectingLedger, setIsSelectingLedger] = useState(false);
  const [currencyLedgers, setCurrencyLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [selectedLedgerId, setSelectedLedgerId] = useState<string | null>(null);
  const [useCurrencyLedger, setUseCurrencyLedger] = useState(false);

  // 當使用者進入「選擇外幣帳本」步驟時，載入帳本清單供選擇。
  useEffect(() => {
    if (isSelectingLedger) {
      currencyLedgerApi.getAll().then(setCurrencyLedgers).catch(console.error);
    }
  }, [isSelectingLedger]);

  /**
   * 開啟匯入流程：先讓使用者決定是否使用外幣帳本。
   */
  const handleOpenImport = () => {
    setIsSelectingLedger(true);
  };

  /**
   * 選擇外幣帳本（或不使用帳本），然後開啟 CSVImportModal。
   *
   * @param ledgerId 選擇的 ledgerId；`null` 表示不使用外幣帳本
   */
  const handleSelectLedger = (ledgerId: string | null) => {
    setSelectedLedgerId(ledgerId);
    setUseCurrencyLedger(ledgerId !== null);
    setIsSelectingLedger(false);
    setIsModalOpen(true);
  };

  /**
   * 實際匯入：逐列解析/驗證後呼叫 API 建立交易。
   *
   * 設計重點：
   * - 先依日期排序，避免 sell-before-buy 的順序問題。
   * - 台股（ticker 以數字開頭）一律不使用外幣帳本，並要求有匯率（通常為 1）。
   */
  const handleImport = async (
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
      const rowNum = originalIndex + 2; // 1-based, skip header row

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
        const isTaiwanStock = normalizedTicker !== '' && /^\d/.test(normalizedTicker);
        const useLedgerForRow = useCurrencyLedger && !isTaiwanStock;

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

        // Parse exchange rate
        // - Taiwan stocks always require exchange rate (typically 1)
        // - When using currency ledger, exchange rate is optional for non-TW tickers (backend will calculate)
        const exchangeRateStr = getRowValue(row, csvData.headers, mapping, 'exchangeRate');
        let exchangeRate: number | undefined;

        if (exchangeRateStr) {
          const parsed = parseNumber(exchangeRateStr);
          if (parsed === null || parsed <= 0) {
            errors.push({ row: rowNum, column: '匯率', message: `無效的匯率: ${exchangeRateStr}` });
            continue;
          }
          exchangeRate = parsed;
        } else if (isTaiwanStock) {
          // Taiwan stocks are priced in TWD; always use exchange rate = 1
          exchangeRate = 1;
        } else if (!useLedgerForRow) {
          errors.push({ row: rowNum, column: '匯率', message: '匯率為必填欄位（未選擇外幣帳本時）' });
          continue;
        }
        // Parse optional fields
        const feesStr = getRowValue(row, csvData.headers, mapping, 'fees');
        const notes = getRowValue(row, csvData.headers, mapping, 'notes');

        const fees = feesStr ? parseNumber(feesStr) ?? 0 : 0;

        // Build request
        const request: CreateStockTransactionRequest = {
          portfolioId,
          ticker: normalizedTicker,
          transactionType,
          transactionDate: formatDateISO(parsedDate),
          shares: Math.abs(shares),
          pricePerShare: Math.abs(price),
          exchangeRate, // undefined only when using currency ledger for non-TW tickers
          fees,
          fundSource: useLedgerForRow ? FundSource.CurrencyLedger : FundSource.None,
          currencyLedgerId: useLedgerForRow ? (selectedLedgerId ?? undefined) : undefined,
          notes: notes || undefined,
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
      onImportComplete();
    }

    return {
      success: errors.length === 0,
      errors,
    };
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

      {/* Currency Ledger Selection Modal */}
      {isSelectingLedger && (
        <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
          <div className="card-dark p-6 w-full max-w-md m-4">
            <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4">選擇匯率來源</h2>
            <p className="text-[var(--text-muted)] mb-4">
              您可以選擇從外幣帳本自動計算匯率，或在 CSV 中手動提供匯率。
            </p>

            <div className="space-y-3">
              <button
                onClick={() => handleSelectLedger(null)}
                className="w-full p-4 border border-[var(--border-color)] rounded-lg hover:bg-[var(--bg-hover)] text-left transition-colors"
              >
                <div className="font-medium text-[var(--text-primary)]">手動提供匯率</div>
                <div className="text-sm text-[var(--text-muted)]">CSV 中需包含匯率欄位</div>
              </button>

              {currencyLedgers.length > 0 && (
                <div className="border-t border-[var(--border-color)] pt-3">
                  <div className="text-sm text-[var(--text-muted)] mb-2">或從外幣帳本自動計算：</div>
                  {currencyLedgers.map((ledgerSummary) => (
                    <button
                      key={ledgerSummary.ledger.id}
                      onClick={() => handleSelectLedger(ledgerSummary.ledger.id)}
                      className="w-full p-4 border border-[var(--border-color)] rounded-lg hover:bg-[var(--accent-peach-soft)] hover:border-[var(--accent-peach)] text-left flex items-center gap-3 mb-2 transition-colors"
                    >
                      <Wallet className="w-5 h-5 text-[var(--accent-peach)]" />
                      <div>
                        <div className="font-medium text-[var(--text-primary)]">{ledgerSummary.ledger.currencyCode}</div>
                        <div className="text-sm text-[var(--text-muted)]">{ledgerSummary.ledger.name}</div>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </div>

            <button
              onClick={() => setIsSelectingLedger(false)}
              className="w-full mt-4 py-2 text-[var(--text-muted)] hover:text-[var(--text-primary)] transition-colors"
            >
              取消
            </button>
          </div>
        </div>
      )}

      <CSVImportModal
        isOpen={isModalOpen}
        onClose={() => {
          setIsModalOpen(false);
          setSelectedLedgerId(null);
          setUseCurrencyLedger(false);
        }}
        title={useCurrencyLedger ? "匯入股票交易（自動計算匯率）" : "匯入股票交易"}
        fields={getStockFields(useCurrencyLedger)}
        onImport={handleImport}
      />
    </>
  );
}
