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
  1: 'bg-green-100 text-green-800',
  2: 'bg-red-100 text-red-800',
  3: 'bg-purple-100 text-purple-800',
  4: 'bg-gray-100 text-gray-800',
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

  if (transactions.length === 0) {
    return (
      <div className="text-center py-8 text-gray-500">
        尚無交易紀錄，請在上方新增第一筆交易。
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-4 py-3 text-left font-medium text-gray-600">日期</th>
            <th className="px-4 py-3 text-left font-medium text-gray-600">股票代號</th>
            <th className="px-4 py-3 text-left font-medium text-gray-600">類型</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">股數</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">價格</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">匯率</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">手續費</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">總成本 (TWD)</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">已實現損益</th>
            {(onEdit || onDelete) && (
              <th className="px-4 py-3 text-center font-medium text-gray-600">操作</th>
            )}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {transactions.map((tx) => (
            <tr key={tx.id} className="hover:bg-gray-50">
              <td className="px-4 py-3 text-gray-700">
                {formatDate(tx.transactionDate)}
              </td>
              <td className="px-4 py-3 font-medium text-gray-900">{tx.ticker}</td>
              <td className="px-4 py-3">
                <span
                  className={`px-2 py-1 rounded-full text-xs font-medium ${
                    transactionTypeColors[tx.transactionType]
                  }`}
                >
                  {transactionTypeLabels[tx.transactionType]}
                </span>
              </td>
              <td className="px-4 py-3 text-right text-gray-700">
                {formatNumber(tx.shares, 4)}
              </td>
              <td className="px-4 py-3 text-right text-gray-700">
                {formatNumber(tx.pricePerShare, 5)}
              </td>
              <td className="px-4 py-3 text-right text-gray-700">
                {formatNumber(tx.exchangeRate, 4)}
              </td>
              <td className="px-4 py-3 text-right text-gray-700">
                {formatNumber(tx.fees)}
              </td>
              <td className="px-4 py-3 text-right font-medium text-gray-900">
                {formatNumber(tx.totalCostHome)}
              </td>
              <td className="px-4 py-3 text-right">
                {tx.realizedPnlHome != null ? (
                  <span
                    className={`font-medium ${
                      tx.realizedPnlHome >= 0 ? 'text-green-600' : 'text-red-600'
                    }`}
                  >
                    {tx.realizedPnlHome >= 0 ? '+' : ''}{formatNumber(tx.realizedPnlHome)}
                  </span>
                ) : (
                  <span className="text-gray-400">-</span>
                )}
              </td>
              {(onEdit || onDelete) && (
                <td className="px-4 py-3 text-center">
                  <div className="flex justify-center gap-2">
                    {onEdit && (
                      <button
                        onClick={() => onEdit(tx)}
                        className="text-blue-600 hover:text-blue-800"
                      >
                        編輯
                      </button>
                    )}
                    {onDelete && (
                      <button
                        onClick={() => onDelete(tx.id)}
                        className="text-red-600 hover:text-red-800"
                      >
                        刪除
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
