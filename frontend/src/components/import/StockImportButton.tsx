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
  portfolioId: string;
  onImportComplete: () => void;
}

// Field definitions for stock transaction CSV (exchangeRate is optional when using currency ledger)
const getStockFields = (useCurrencyLedger: boolean): FieldDefinition[] => [
  {
    name: 'date',
    label: '日期',
    aliases: ['transactionDate', 'transaction_date', 'Date', '交易日期', '日期', '買進日期'],
    required: true,
  },
  {
    name: 'ticker',
    label: '股票代號',
    aliases: ['Ticker', 'symbol', 'Symbol', 'stock', 'Stock', '代碼', '股票'],
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
    aliases: ['Shares', 'quantity', 'Quantity', 'qty', 'Qty', '數量', '股', '買進股數'],
    required: true,
  },
  {
    name: 'price',
    label: '每股價格',
    aliases: ['pricePerShare', 'price_per_share', 'Price', '價格', '單價', '買進價格'],
    required: true,
  },
  {
    name: 'exchangeRate',
    label: '匯率',
    aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', '匯率'],
    required: !useCurrencyLedger, // Optional when using currency ledger
  },
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
  },
];

// Map transaction type string to enum
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
}: StockImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isSelectingLedger, setIsSelectingLedger] = useState(false);
  const [currencyLedgers, setCurrencyLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [selectedLedgerId, setSelectedLedgerId] = useState<string | null>(null);
  const [useCurrencyLedger, setUseCurrencyLedger] = useState(false);

  // Load currency ledgers when selecting
  useEffect(() => {
    if (isSelectingLedger) {
      currencyLedgerApi.getAll().then(setCurrencyLedgers).catch(console.error);
    }
  }, [isSelectingLedger]);

  const handleOpenImport = () => {
    setIsSelectingLedger(true);
  };

  const handleSelectLedger = (ledgerId: string | null) => {
    setSelectedLedgerId(ledgerId);
    setUseCurrencyLedger(ledgerId !== null);
    setIsSelectingLedger(false);
    setIsModalOpen(true);
  };

  const handleImport = async (
    csvData: ParsedCSV,
    mapping: ColumnMapping
  ): Promise<{ success: boolean; errors: ParseError[] }> => {
    const errors: ParseError[] = [];
    let successCount = 0;

    for (let i = 0; i < csvData.rows.length; i++) {
      const row = csvData.rows[i];
      const rowNum = i + 2; // 1-based, skip header row

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

        // Parse exchange rate (optional when using currency ledger)
        const exchangeRateStr = getRowValue(row, csvData.headers, mapping, 'exchangeRate');
        let exchangeRate: number | undefined;

        if (exchangeRateStr) {
          exchangeRate = parseNumber(exchangeRateStr) ?? undefined;
          if (exchangeRate !== undefined && exchangeRate <= 0) {
            errors.push({ row: rowNum, column: '匯率', message: `無效的匯率: ${exchangeRateStr}` });
            continue;
          }
        } else if (!useCurrencyLedger) {
          // Exchange rate is required when not using currency ledger
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
          ticker: ticker.toUpperCase().trim(),
          transactionType,
          transactionDate: formatDateISO(parsedDate),
          shares: Math.abs(shares),
          pricePerShare: Math.abs(price),
          exchangeRate, // undefined when using currency ledger - backend will calculate
          fees,
          fundSource: useCurrencyLedger ? FundSource.CurrencyLedger : FundSource.None,
          currencyLedgerId: selectedLedgerId ?? undefined,
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
      <button
        onClick={handleOpenImport}
        className="btn-dark flex items-center gap-2"
      >
        <Upload className="w-4 h-4" />
        匯入 CSV
      </button>

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
