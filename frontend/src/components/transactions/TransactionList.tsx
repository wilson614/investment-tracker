import type { StockTransaction, TransactionType } from '../../types';

interface TransactionListProps {
  transactions: StockTransaction[];
  onDelete?: (id: string) => Promise<void>;
  onEdit?: (transaction: StockTransaction) => void;
}

const transactionTypeLabels: Record<TransactionType, string> = {
  1: 'Buy',
  2: 'Sell',
  3: 'Split',
  4: 'Adjustment',
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
        No transactions yet. Add your first transaction above.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-4 py-3 text-left font-medium text-gray-600">Date</th>
            <th className="px-4 py-3 text-left font-medium text-gray-600">Ticker</th>
            <th className="px-4 py-3 text-left font-medium text-gray-600">Type</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Shares</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Price</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Rate</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Fees</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Total (TWD)</th>
            <th className="px-4 py-3 text-right font-medium text-gray-600">Realized PnL</th>
            {(onEdit || onDelete) && (
              <th className="px-4 py-3 text-center font-medium text-gray-600">Actions</th>
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
                        Edit
                      </button>
                    )}
                    {onDelete && (
                      <button
                        onClick={() => onDelete(tx.id)}
                        className="text-red-600 hover:text-red-800"
                      >
                        Delete
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
