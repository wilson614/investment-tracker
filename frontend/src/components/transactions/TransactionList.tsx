/**
 * TransactionList
 *
 * 交易清單表格：以表格呈現交易明細，並支援可選的編輯/刪除操作。
 *
 * 特色：
 * - 支援股票分割調整顯示（FR-052a）：原始股數/價格會以刪除線顯示，並顯示調整後數值。
 * - 當 `totalCostHome` 為 null 時，代表沒有匯率資料，改顯示 source currency 成本。
 */
import { Pencil, Trash2, SplitSquareHorizontal } from 'lucide-react';
import type { StockTransaction, TransactionType, StockMarket } from '../../types';
import { StockMarket as StockMarketEnum } from '../../types';
import { formatFullDate } from '../../utils/dateUtils';

interface TransactionListProps {
  /** 要顯示的交易清單 */
  transactions: StockTransaction[];
  /** 刪除 callback（若提供則顯示刪除按鈕） */
  onDelete?: (id: string) => Promise<void>;
  /** 編輯 callback（若提供則顯示編輯按鈕） */
  onEdit?: (transaction: StockTransaction) => void;
}

const transactionTypeLabels: Record<TransactionType, string> = {
  1: '買入',
  2: '賣出',
  3: '股票分割',
  4: '調整',
};

const transactionTypeColors: Record<TransactionType, string> = {
  1: 'badge-success',
  2: 'badge-danger',
  3: 'badge-cream',
  4: 'badge-butter',
};

const marketLabels: Record<StockMarket, string> = {
  [StockMarketEnum.TW]: '台',
  [StockMarketEnum.US]: '美',
  [StockMarketEnum.UK]: '英',
  [StockMarketEnum.EU]: '歐',
};

export function TransactionList({ transactions, onDelete, onEdit }: TransactionListProps) {
  const formatNumber = (value: number, decimals = 2) => {
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format TWD as integer (no decimals)
  const formatTWD = (value: number) => {
    return Math.round(value).toLocaleString('zh-TW');
  };

  /**
   * 顯示股數：若交易有 split adjustment，會同時顯示原始值與調整後值（FR-052a）。
   */
  const renderShares = (tx: StockTransaction) => {
    if (tx.hasSplitAdjustment && tx.adjustedShares != null) {
      return (
        <div className="flex flex-col items-end">
          <span className="text-[var(--text-muted)] text-xs line-through">
            {formatNumber(tx.shares, 4)}
          </span>
          <span className="flex items-center gap-1">
            <SplitSquareHorizontal className="w-3 h-3 text-[var(--accent-cream)]" />
            {formatNumber(tx.adjustedShares, 4)}
          </span>
        </div>
      );
    }
    return formatNumber(tx.shares, 4);
  };

  /**
   * 顯示價格：若交易有 split adjustment，會同時顯示原始值與調整後值（FR-052a）。
   */
  const renderPrice = (tx: StockTransaction) => {
    if (tx.hasSplitAdjustment && tx.adjustedPricePerShare != null) {
      return (
        <div className="flex flex-col items-end">
          <span className="text-[var(--text-muted)] text-xs line-through">
            {formatNumber(tx.pricePerShare, 5)}
          </span>
          <span className="flex items-center gap-1">
            <SplitSquareHorizontal className="w-3 h-3 text-[var(--accent-cream)]" />
            {formatNumber(tx.adjustedPricePerShare, 5)}
          </span>
        </div>
      );
    }
    return formatNumber(tx.pricePerShare, 5);
  };

  if (transactions.length === 0) {
    return (
      <div className="text-center py-12 text-[var(--text-muted)] text-base">
        尚無交易紀錄，請在上方新增第一筆交易。
      </div>
    );
  }

  return (
    <div className="overflow-x-auto max-h-[60vh] overflow-y-auto">
      <table className="table-dark">
        <thead className="sticky top-0 z-10">
          <tr>
            <th>日期</th>
            <th>股票代號</th>
            <th>類型</th>
            <th className="text-right">股數</th>
            <th className="text-right">價格</th>
            <th className="text-right">匯率</th>
            <th className="text-right">手續費</th>
            <th className="text-right">總成本</th>
            <th className="text-right">已實現損益</th>
            {(onEdit || onDelete) && (
              <th className="w-24 text-center">操作</th>
            )}
          </tr>
        </thead>
        <tbody>
          {transactions.map((tx) => (
            <tr key={tx.id}>
              <td className="whitespace-nowrap">
                {formatFullDate(tx.transactionDate)}
              </td>
              <td className="font-medium text-[var(--accent-cream)]">
                {tx.ticker}
                <span className="ml-1 text-xs text-[var(--text-muted)]">
                  ({marketLabels[tx.market] || '?'})
                </span>
              </td>
              <td>
                <span className={`badge ${transactionTypeColors[tx.transactionType]}`}>
                  {transactionTypeLabels[tx.transactionType]}
                </span>
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {renderShares(tx)}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {renderPrice(tx)}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {tx.exchangeRate != null ? formatNumber(tx.exchangeRate, 4) : '-'}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {formatNumber(tx.fees)}
              </td>
              <td className="text-right font-medium number-display whitespace-nowrap">
                {tx.totalCostHome != null
                  ? `${formatTWD(tx.totalCostHome)} TWD`
                  : formatNumber(tx.totalCostSource, 2)}
              </td>
              <td className="text-right">
                {tx.realizedPnlHome != null ? (
                  <span
                    className={`font-medium number-display ${
                      tx.realizedPnlHome >= 0 ? 'number-positive' : 'number-negative'
                    }`}
                  >
                    {tx.realizedPnlHome >= 0 ? '+' : ''}{formatTWD(tx.realizedPnlHome)}
                  </span>
                ) : (
                  <span className="text-[var(--text-muted)]">-</span>
                )}
              </td>
              {(onEdit || onDelete) && (
                <td className="text-center">
                  <div className="flex justify-center gap-2">
                    {onEdit && (
                      <button
                        onClick={() => onEdit(tx)}
                        className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                        title="編輯"
                      >
                        <Pencil className="w-4 h-4" />
                      </button>
                    )}
                    {onDelete && (
                      <button
                        onClick={() => onDelete(tx.id)}
                        className="p-1.5 text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                        title="刪除"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </div>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
