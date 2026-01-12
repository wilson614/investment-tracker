import { useState, useEffect } from 'react';
import { transactionApi, portfolioApi } from '../services/api';
import { TransactionList } from '../components/transactions/TransactionList';
import type { StockTransaction } from '../types';

export function TransactionsPage() {
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState({
    ticker: '',
    type: '',
  });

  useEffect(() => {
    const loadTransactions = async () => {
      try {
        setIsLoading(true);

        // Get user's portfolio
        const portfolios = await portfolioApi.getAll();
        if (portfolios.length === 0) {
          setError('找不到投資組合');
          return;
        }
        const portfolioId = portfolios[0].id;

        const data = await transactionApi.getByPortfolio(portfolioId);
        setTransactions(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load transactions');
      } finally {
        setIsLoading(false);
      }
    };

    loadTransactions();
  }, []);

  const handleDelete = async (id: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) {
      return;
    }
    await transactionApi.delete(id);
    setTransactions((prev) => prev.filter((t) => t.id !== id));
  };

  const filteredTransactions = transactions.filter((tx) => {
    if (filter.ticker && !tx.ticker.toLowerCase().includes(filter.ticker.toLowerCase())) {
      return false;
    }
    if (filter.type && tx.transactionType !== Number(filter.type)) {
      return false;
    }
    return true;
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--color-danger)] text-lg">{error}</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">交易紀錄</h1>

        {/* Filters */}
        <div className="card-dark p-5 mb-6">
          <div className="flex gap-4">
            <div className="flex-1">
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                股票代號篩選
              </label>
              <input
                type="text"
                value={filter.ticker}
                onChange={(e) => setFilter((prev) => ({ ...prev, ticker: e.target.value }))}
                placeholder="例如：VWRA"
                className="input-dark w-full"
              />
            </div>
            <div className="w-40">
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                類型
              </label>
              <select
                value={filter.type}
                onChange={(e) => setFilter((prev) => ({ ...prev, type: e.target.value }))}
                className="input-dark w-full"
              >
                <option value="">全部</option>
                <option value="1">買入</option>
                <option value="2">賣出</option>
              </select>
            </div>
          </div>
        </div>

        {/* Summary */}
        <div className="mb-4 text-base text-[var(--text-muted)]">
          顯示 {filteredTransactions.length} 筆，共 {transactions.length} 筆交易
        </div>

        {/* Transaction List */}
        <div className="card-dark overflow-hidden">
          <TransactionList
            transactions={filteredTransactions}
            onDelete={handleDelete}
          />
        </div>
      </div>
    </div>
  );
}
