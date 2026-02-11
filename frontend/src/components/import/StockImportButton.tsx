/**
 * StockImportButton
 *
 * 股票交易 CSV 匯入按鈕：開啟 CSVImportModal → 逐列解析/驗證 → 呼叫交易 API。
 *
 * 重要行為：
 * - 會先將 CSV rows 依日期排序，避免出現 sell-before-buy 的計算/驗證問題。
 * - 交易會自動連結到 Portfolio 綁定的帳本（FR-001）。
 * - 匯率欄位為可選：若 CSV 未提供，backend 會自動從交易日期抓取。
 */
import { useState } from 'react';
import { Upload } from 'lucide-react';
import { CSVImportModal, type FieldDefinition } from './CSVImportModal';
import { transactionApi } from '../../services/api';
import {
  getRowValue,
  parseDate,
  parseNumber,
  formatDateISO,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';
import { StockMarket, Currency } from '../../types';
import type { CreateStockTransactionRequest, TransactionType, StockMarket as StockMarketType, Currency as CurrencyType } from '../../types';

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
 * 匯率欄位為選填：若 CSV 未提供，backend 會自動從交易日期抓取。
 * 台股會自動設為匯率 = 1。
 */
const stockFields: FieldDefinition[] = [
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
    name: 'market',
    label: '市場',
    aliases: ['Market', 'exchange', 'Exchange', '市場', '交易所'],
    required: true,
  },
  {
    name: 'currency',
    label: '幣別',
    aliases: ['Currency', 'currencyCode', 'currency_code', '幣別', '貨幣'],
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
  {
    name: 'exchangeRate',
    label: '匯率（選填）',
    aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', '匯率'],
    required: false,
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

export function StockImportButton({
  portfolioId,
  onImportComplete,
  compact = false,
  renderTrigger,
}: StockImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  /**
   * 開啟匯入 modal。
   */
  const handleOpenImport = () => {
    setIsModalOpen(true);
  };

  /**
   * 實際匯入：逐列解析/驗證後呼叫 API 建立交易。
   *
   * 設計重點：
   * - 先依日期排序，避免 sell-before-buy 的順序問題。
   * - 台股自動設定匯率 = 1。
   * - 非台股若無匯率，backend 會自動從交易日期抓取。
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
        const currency = parseCurrency(currencyStr);
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

        const fees = feesStr ? parseNumber(feesStr) ?? 0 : 0;

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

      <CSVImportModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="匯入股票交易"
        fields={stockFields}
        onImport={handleImport}
      />
    </>
  );
}
