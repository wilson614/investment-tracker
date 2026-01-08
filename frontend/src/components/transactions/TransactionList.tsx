import { Pencil, Trash2 } from 'lucide-react';
import type { StockTransaction, TransactionType } from '../../types';

interface TransactionListProps {
  transactions: StockTransaction[];
  onDelete?: (id: string) => Promise<void>;
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

export function TransactionList({ transactions, onDelete, onEdit }: TransactionListProps) {
  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('zh-TW', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  };

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
            <th className="text-right">總成本 (TWD)</th>
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
                {formatDate(tx.transactionDate)}
              </td>
              <td className="font-medium text-[var(--accent-cream)]">{tx.ticker}</td>
              <td>
                <span className={`badge ${transactionTypeColors[tx.transactionType]}`}>
                  {transactionTypeLabels[tx.transactionType]}
                </span>
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {formatNumber(tx.shares, 4)}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {formatNumber(tx.pricePerShare, 5)}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {formatNumber(tx.exchangeRate, 4)}
              </td>
              <td className="text-right number-display whitespace-nowrap">
                {formatNumber(tx.fees)}
              </td>
              <td className="text-right font-medium number-display whitespace-nowrap">
                {formatTWD(tx.totalCostHome)}
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
