import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { currencyLedgerApi, currencyTransactionApi } from '../services/api';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import type { CurrencyLedgerSummary, CurrencyTransaction, CreateCurrencyTransactionRequest } from '../types';
import { CurrencyTransactionType } from '../types';

const transactionTypeLabels: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: '換匯買入',
  [CurrencyTransactionType.ExchangeSell]: '換匯賣出',
  [CurrencyTransactionType.Interest]: '利息收入',
  [CurrencyTransactionType.Spend]: '消費支出',
};

export default function CurrencyDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [ledger, setLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [transactions, setTransactions] = useState<CurrencyTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showAddForm, setShowAddForm] = useState(false);

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

  const handleDelete = async (txId: string) => {
    if (!confirm('確定要刪除這筆交易嗎？')) return;
    try {
      await currencyTransactionApi.delete(txId);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete');
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
          <button
            onClick={() => setShowAddForm(true)}
            className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            新增交易
          </button>
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

      {/* Transaction List */}
      <div className="bg-white rounded-lg shadow-lg p-6">
        <h2 className="text-lg font-bold mb-4">交易紀錄</h2>

        {transactions.length === 0 ? (
          <p className="text-gray-500 text-center py-8">尚無交易紀錄</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b text-left text-sm text-gray-500">
                  <th className="pb-3">日期</th>
                  <th className="pb-3">類型</th>
                  <th className="pb-3 text-right">外幣金額</th>
                  <th className="pb-3 text-right">台幣金額</th>
                  <th className="pb-3 text-right">匯率</th>
                  <th className="pb-3">備註</th>
                  <th className="pb-3"></th>
                </tr>
              </thead>
              <tbody>
                {transactions.map((tx) => (
                  <tr key={tx.id} className="border-b hover:bg-gray-50">
                    <td className="py-3">{formatDate(tx.transactionDate)}</td>
                    <td className="py-3">
                      <span className={`inline-block px-2 py-1 rounded text-xs font-medium ${
                        tx.transactionType === CurrencyTransactionType.ExchangeBuy
                          ? 'bg-green-100 text-green-800'
                          : tx.transactionType === CurrencyTransactionType.ExchangeSell
                          ? 'bg-red-100 text-red-800'
                          : tx.transactionType === CurrencyTransactionType.Interest
                          ? 'bg-blue-100 text-blue-800'
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
                    <td className="py-3 text-gray-500 text-sm max-w-32 truncate">
                      {tx.notes || '-'}
                    </td>
                    <td className="py-3">
                      <button
                        onClick={() => handleDelete(tx.id)}
                        className="text-red-500 hover:text-red-700 text-sm"
                      >
                        刪除
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
