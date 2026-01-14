/**
 * CSV Export Service
 * Exports transactions and positions to CSV with UTF-8 BOM for Excel compatibility
 */

import type { StockTransaction, StockPosition, CurrencyTransaction } from '../types';
import { TransactionType, CurrencyTransactionType } from '../types';

// UTF-8 BOM for Excel compatibility
const UTF8_BOM = '\uFEFF';

// Transaction type labels in Chinese
const TRANSACTION_TYPE_LABELS: Record<number, string> = {
  [TransactionType.Buy]: '買入',
  [TransactionType.Sell]: '賣出',
  [TransactionType.Split]: '分割',
  [TransactionType.Adjustment]: '調整',
};

// Currency transaction type labels in Chinese
const CURRENCY_TX_TYPE_LABELS: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: '換匯買入',
  [CurrencyTransactionType.ExchangeSell]: '換匯賣出',
  [CurrencyTransactionType.Interest]: '利息收入',
  [CurrencyTransactionType.Spend]: '消費支出',
  [CurrencyTransactionType.InitialBalance]: '轉入餘額',
  [CurrencyTransactionType.OtherIncome]: '其他收入',
  [CurrencyTransactionType.OtherExpense]: '其他支出',
};

/**
 * Escape CSV field value
 * - Wrap in quotes if contains comma, newline, or quotes
 * - Escape quotes by doubling them
 */
function escapeCSVField(value: string | number | null | undefined): string {
  if (value == null) return '';
  const str = String(value);
  if (str.includes(',') || str.includes('\n') || str.includes('"')) {
    return `"${str.replace(/"/g, '""')}"`;
  }
  return str;
}

/**
 * Format date as YYYY-MM-DD
 */
function formatDate(dateStr: string): string {
  const date = new Date(dateStr);
  return date.toISOString().split('T')[0];
}

/**
 * Format number with fixed decimals
 */
function formatNumber(value: number | null | undefined, decimals = 2): string {
  if (value == null) return '';
  return value.toFixed(decimals);
}

/**
 * Generate CSV content for transactions
 */
export function generateTransactionsCsv(
  transactions: StockTransaction[],
  _baseCurrency = 'USD',
  homeCurrency = 'TWD'
): string {
  // CSV Headers in Chinese
  const headers = [
    '日期',
    '股票代號',
    '類型',
    '股數',
    '價格（原幣）',
    '手續費（原幣）',
    '匯率',
    '總成本（原幣）',
    `總成本（${homeCurrency}）`,
    `已實現損益（${homeCurrency}）`,
    '備註',
  ];

  // Generate rows
  const rows = transactions.map((tx) => [
    escapeCSVField(formatDate(tx.transactionDate)),
    escapeCSVField(tx.ticker),
    escapeCSVField(TRANSACTION_TYPE_LABELS[tx.transactionType] || String(tx.transactionType)),
    escapeCSVField(formatNumber(tx.shares, 4)),
    escapeCSVField(formatNumber(tx.pricePerShare, 4)),
    escapeCSVField(formatNumber(tx.fees, 2)),
    escapeCSVField(formatNumber(tx.exchangeRate, 6)),
    escapeCSVField(formatNumber(tx.totalCostSource, 2)),
    escapeCSVField(formatNumber(tx.totalCostHome, 2)),
    escapeCSVField(tx.realizedPnlHome != null ? formatNumber(tx.realizedPnlHome, 2) : ''),
    escapeCSVField(tx.notes || ''),
  ]);

  // Combine headers and rows
  const csvContent = [
    headers.join(','),
    ...rows.map((row) => row.join(',')),
  ].join('\n');

  return UTF8_BOM + csvContent;
}

/**
 * Generate CSV content for positions
 */
export function generatePositionsCsv(
  positions: StockPosition[],
  _baseCurrency = 'USD',
  homeCurrency = 'TWD'
): string {
  // CSV Headers in Chinese
  const headers = [
    '股票代號',
    '持股數量',
    '平均成本（原幣）',
    `總成本（${homeCurrency}）`,
    '現價（原幣）',
    `市值（${homeCurrency}）`,
    `未實現損益（${homeCurrency}）`,
    '報酬率（%）',
  ];

  // Generate rows
  const rows = positions.map((pos) => [
    escapeCSVField(pos.ticker),
    escapeCSVField(formatNumber(pos.totalShares, 4)),
    escapeCSVField(formatNumber(pos.averageCostPerShareSource, 4)),
    escapeCSVField(formatNumber(pos.totalCostHome, 2)),
    escapeCSVField(pos.currentPrice != null ? formatNumber(pos.currentPrice, 4) : ''),
    escapeCSVField(pos.currentValueHome != null ? formatNumber(pos.currentValueHome, 2) : ''),
    escapeCSVField(pos.unrealizedPnlHome != null ? formatNumber(pos.unrealizedPnlHome, 2) : ''),
    escapeCSVField(pos.unrealizedPnlPercentage != null ? formatNumber(pos.unrealizedPnlPercentage, 2) : ''),
  ]);

  // Combine headers and rows
  const csvContent = [
    headers.join(','),
    ...rows.map((row) => row.join(',')),
  ].join('\n');

  return UTF8_BOM + csvContent;
}

/**
 * Download CSV file
 */
export function downloadCsv(content: string, filename: string): void {
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.style.display = 'none';

  document.body.appendChild(link);
  link.click();

  // Cleanup
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/**
 * Export transactions to CSV and trigger download
 */
export function exportTransactionsToCsv(
  transactions: StockTransaction[],
  baseCurrency = 'USD',
  homeCurrency = 'TWD',
  filename?: string
): void {
  const csv = generateTransactionsCsv(transactions, baseCurrency, homeCurrency);
  const defaultFilename = `交易紀錄_${new Date().toISOString().split('T')[0]}.csv`;
  downloadCsv(csv, filename || defaultFilename);
}

/**
 * Export positions to CSV and trigger download
 */
export function exportPositionsToCsv(
  positions: StockPosition[],
  baseCurrency = 'USD',
  homeCurrency = 'TWD',
  filename?: string
): void {
  const csv = generatePositionsCsv(positions, baseCurrency, homeCurrency);
  const defaultFilename = `持倉明細_${new Date().toISOString().split('T')[0]}.csv`;
  downloadCsv(csv, filename || defaultFilename);
}

/**
 * Generate CSV content for currency transactions
 */
export function generateCurrencyTransactionsCsv(
  transactions: CurrencyTransaction[],
  _currencyCode: string,
  _homeCurrency = 'TWD'
): string {
  // CSV Headers in Chinese (match table columns without parentheses)
  const headers = [
    '日期',
    '類型',
    '外幣金額',
    '台幣金額',
    '匯率',
    '備註',
  ];

  // Generate rows
  const rows = transactions.map((tx) => [
    escapeCSVField(formatDate(tx.transactionDate)),
    escapeCSVField(CURRENCY_TX_TYPE_LABELS[tx.transactionType] || String(tx.transactionType)),
    escapeCSVField(formatNumber(tx.foreignAmount, 4)),
    escapeCSVField(tx.homeAmount != null ? formatNumber(tx.homeAmount, 2) : ''),
    escapeCSVField(tx.exchangeRate != null ? formatNumber(tx.exchangeRate, 4) : ''),
    escapeCSVField(tx.notes || ''),
  ]);

  // Combine headers and rows
  const csvContent = [
    headers.join(','),
    ...rows.map((row) => row.join(',')),
  ].join('\n');

  return UTF8_BOM + csvContent;
}

/**
 * Export currency transactions to CSV and trigger download
 */
export function exportCurrencyTransactionsToCsv(
  transactions: CurrencyTransaction[],
  currencyCode: string,
  homeCurrency = 'TWD',
  filename?: string
): void {
  const filtered = transactions.filter(
    (tx) => !(tx.transactionType === CurrencyTransactionType.Spend && tx.relatedStockTransactionId)
  );
  const csv = generateCurrencyTransactionsCsv(filtered, currencyCode, homeCurrency);
  const defaultFilename = `${currencyCode}_交易紀錄_${new Date().toISOString().split('T')[0]}.csv`;
  downloadCsv(csv, filename || defaultFilename);
}
