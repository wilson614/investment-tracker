import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { currencyLedgerApi, currencyTransactionApi } from '../services/api';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import { CurrencyImportButton } from '../components/import';
import type { CurrencyLedgerSummary, CurrencyTransaction, CreateCurrencyTransactionRequest } from '../types';
import { CurrencyTransactionType } from '../types';

const transactionTypeLabels: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: '換匯買入',
  [CurrencyTransactionType.ExchangeSell]: '換匯賣出',
  [CurrencyTransactionType.Interest]: '利息收入',
  [CurrencyTransactionType.Spend]: '消費支出',
  [CurrencyTransactionType.InitialBalance]: '期初餘額',
  [CurrencyTransactionType.OtherIncome]: '其他收入',
  [CurrencyTransactionType.OtherExpense]: '其他支出',
};

// Calculate balance change for a transaction
function getBalanceChange(tx: CurrencyTransaction): number {
  switch (tx.transactionType) {
    case CurrencyTransactionType.ExchangeBuy:
    case CurrencyTransactionType.InitialBalance:
    case CurrencyTransactionType.Interest:
    case CurrencyTransactionType.OtherIncome:
      return tx.foreignAmount;
    case CurrencyTransactionType.ExchangeSell:
    case CurrencyTransactionType.Spend:
    case CurrencyTransactionType.OtherExpense:
      return -tx.foreignAmount;
    default:
      return 0;
  }
}

// Calculate running balances for transactions (sorted by date)
function calculateRunningBalances(transactions: CurrencyTransaction[]): Map<string, number> {
  const balanceMap = new Map<string, number>();
  let runningBalance = 0;

  // Transactions should already be sorted by date from API
  for (const tx of transactions) {
    runningBalance += getBalanceChange(tx);
    balanceMap.set(tx.id, runningBalance);
  }

  return balanceMap;
}

export default function CurrencyDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [ledger, setLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [transactions, setTransactions] = useState<CurrencyTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showAddForm, setShowAddForm] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [isDeleting, setIsDeleting] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const loadData = async () => {
    if (!id) return;
    try {
      setLoading(true);
      const [ledgerData, txData] = await Promise.all([
        currencyLedgerApi.getById(id),
        currencyTransactionApi.getByLedger(id),
      ]);
      setLedger(ledgerData);
      setTransactions(txData);
      setSelectedIds(new Set());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [id]);

  const handleAddTransaction = async (data: CreateCurrencyTransactionRequest) => {
    await currencyTransactionApi.create(data);
    setShowAddForm(false);
    await loadData();
  };

  const handleSelectAll = () => {
    if (selectedIds.size === transactions.length) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(transactions.map(tx => tx.id)));
    }
  };

  const handleSelectOne = (txId: string) => {
    const newSelected = new Set(selectedIds);
    if (newSelected.has(txId)) {
      newSelected.delete(txId);
    } else {
      newSelected.add(txId);
    }
    setSelectedIds(newSelected);
  };

  const handleBatchDelete = async () => {
    setIsDeleting(true);
    try {
      for (const txId of selectedIds) {
        await currencyTransactionApi.delete(txId);
      }
      setShowDeleteConfirm(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete');
    } finally {
      setIsDeleting(false);
    }
  };

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('zh-TW');
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (!ledger) {
    return (
      <div className="container mx-auto px-4 py-8">
        <p className="text-red-500">找不到帳本</p>
        <button onClick={() => navigate('/currency')} className="text-blue-600 underline">
          返回列表
        </button>
      </div>
    );
  }

  const isAllSelected = transactions.length > 0 && selectedIds.size === transactions.length;

  return (
    <div className="container mx-auto px-4 py-8">
      <button
        onClick={() => navigate('/currency')}
        className="flex items-center text-gray-600 hover:text-gray-900 mb-4"
      >
        ← 返回外幣帳本
      </button>

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-lg mb-6">
          {error}
          <button onClick={() => setError(null)} className="ml-2 underline">關閉</button>
        </div>
      )}

      {/* Summary Card */}
      <div className="bg-white rounded-lg shadow-lg p-6 mb-6">
        <div className="flex justify-between items-start mb-4">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {ledger.ledger.currencyCode}
            </h1>
            <p className="text-gray-500">{ledger.ledger.name}</p>
          </div>
          <div className="flex gap-2">
            <CurrencyImportButton
              ledgerId={ledger.ledger.id}
              onImportComplete={loadData}
            />
            <button
              onClick={() => setShowAddForm(true)}
              className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
            >
              新增交易
            </button>
          </div>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div className="bg-gray-50 p-4 rounded-lg">
            <p className="text-sm text-gray-500">餘額</p>
            <p className="text-xl font-bold text-blue-600">
              {formatNumber(ledger.balance)} {ledger.ledger.currencyCode}
            </p>
          </div>
          <div className="bg-gray-50 p-4 rounded-lg">
            <p className="text-sm text-gray-500">加權平均成本</p>
            <p className="text-xl font-bold">
              {formatNumber(ledger.weightedAverageCost, 4)}
            </p>
          </div>
          <div className="bg-gray-50 p-4 rounded-lg">
            <p className="text-sm text-gray-500">總成本</p>
            <p className="text-xl font-bold">
              {formatNumber(ledger.totalCostHome)} {ledger.ledger.homeCurrency}
            </p>
          </div>
          <div className="bg-gray-50 p-4 rounded-lg">
            <p className="text-sm text-gray-500">已實現損益</p>
            <p className={`text-xl font-bold ${ledger.realizedPnl >= 0 ? 'text-green-600' : 'text-red-600'}`}>
              {ledger.realizedPnl >= 0 ? '+' : ''}{formatNumber(ledger.realizedPnl)}
            </p>
          </div>
        </div>
      </div>

      {/* Add Transaction Modal */}
      {showAddForm && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-full max-w-md max-h-[90vh] overflow-y-auto">
            <h2 className="text-xl font-bold mb-4">新增交易</h2>
            <CurrencyTransactionForm
              ledgerId={ledger.ledger.id}
              onSubmit={handleAddTransaction}
              onCancel={() => setShowAddForm(false)}
            />
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-full max-w-md">
            <h2 className="text-xl font-bold mb-4 text-red-600">確認刪除</h2>
            <p className="mb-2">您確定要刪除選取的 <strong>{selectedIds.size}</strong> 筆交易嗎？</p>
            <p className="text-red-500 text-sm mb-4">此操作無法復原！</p>
            <div className="flex gap-3">
              <button
                onClick={() => setShowDeleteConfirm(false)}
                disabled={isDeleting}
                className="flex-1 px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 disabled:opacity-50"
              >
                取消
              </button>
              <button
                onClick={handleBatchDelete}
                disabled={isDeleting}
                className="flex-1 px-4 py-2 text-white bg-red-600 rounded-lg hover:bg-red-700 disabled:opacity-50"
              >
                {isDeleting ? '刪除中...' : '確認刪除'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Transaction List */}
      <div className="bg-white rounded-lg shadow-lg p-6">
        <div className="flex justify-between items-center mb-4">
          <h2 className="text-lg font-bold">交易紀錄</h2>
          {selectedIds.size > 0 && (
            <button
              onClick={() => setShowDeleteConfirm(true)}
              className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 text-sm"
            >
              刪除選取 ({selectedIds.size})
            </button>
          )}
        </div>

        {transactions.length === 0 ? (
          <p className="text-gray-500 text-center py-8">尚無交易紀錄</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b text-left text-sm text-gray-500">
                  <th className="pb-3 pr-2">
                    <input
                      type="checkbox"
                      checked={isAllSelected}
                      onChange={handleSelectAll}
                      className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                  </th>
                  <th className="pb-3">日期</th>
                  <th className="pb-3">類型</th>
                  <th className="pb-3 text-right">外幣金額</th>
                  <th className="pb-3 text-right">台幣金額</th>
                  <th className="pb-3 text-right">匯率</th>
                  <th className="pb-3 text-right">餘額</th>
                  <th className="pb-3">備註</th>
                </tr>
              </thead>
              <tbody>
                {(() => {
                  const runningBalances = calculateRunningBalances(transactions);
                  return transactions.map((tx) => (
                  <tr
                    key={tx.id}
                    className={`border-b hover:bg-gray-50 ${selectedIds.has(tx.id) ? 'bg-blue-50' : ''}`}
                  >
                    <td className="py-3 pr-2">
                      <input
                        type="checkbox"
                        checked={selectedIds.has(tx.id)}
                        onChange={() => handleSelectOne(tx.id)}
                        className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                      />
                    </td>
                    <td className="py-3">{formatDate(tx.transactionDate)}</td>
                    <td className="py-3">
                      <span className={`inline-block px-2 py-1 rounded text-xs font-medium ${
                        tx.transactionType === CurrencyTransactionType.ExchangeBuy
                          ? 'bg-green-100 text-green-800'
                          : tx.transactionType === CurrencyTransactionType.ExchangeSell
                          ? 'bg-red-100 text-red-800'
                          : tx.transactionType === CurrencyTransactionType.Interest ||
                            tx.transactionType === CurrencyTransactionType.OtherIncome
                          ? 'bg-blue-100 text-blue-800'
                          : tx.transactionType === CurrencyTransactionType.Spend ||
                            tx.transactionType === CurrencyTransactionType.OtherExpense
                          ? 'bg-orange-100 text-orange-800'
                          : 'bg-gray-100 text-gray-800'
                      }`}>
                        {transactionTypeLabels[tx.transactionType]}
                      </span>
                    </td>
                    <td className="py-3 text-right font-mono">
                      {formatNumber(tx.foreignAmount, 4)}
                    </td>
                    <td className="py-3 text-right font-mono">
                      {tx.homeAmount ? formatNumber(tx.homeAmount) : '-'}
                    </td>
                    <td className="py-3 text-right font-mono">
                      {tx.exchangeRate ? formatNumber(tx.exchangeRate, 4) : '-'}
                    </td>
                    <td className="py-3 text-right font-mono">
                      {formatNumber(runningBalances.get(tx.id) ?? 0, 4)}
                    </td>
                    <td className="py-3 text-gray-500 text-sm max-w-32 truncate">
                      {tx.notes || '-'}
                    </td>
                  </tr>
                  ));
                })()}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
