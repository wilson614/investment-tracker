import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { transactionApi } from '../services/api';
import { TransactionList } from '../components/transactions/TransactionList';
import type { StockTransaction } from '../types';

export function TransactionsPage() {
  const { portfolioId } = useParams<{ portfolioId: string }>();
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState({
    ticker: '',
    type: '',
  });

  useEffect(() => {
    if (!portfolioId) return;

    const loadTransactions = async () => {
      try {
        setIsLoading(true);
        const data = await transactionApi.getByPortfolio(portfolioId);
        setTransactions(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load transactions');
      } finally {
        setIsLoading(false);
      }
    };

    loadTransactions();
  }, [portfolioId]);

  const handleDelete = async (id: string) => {
    if (!window.confirm('Are you sure you want to delete this transaction?')) {
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
        <div className="text-gray-500">Loading...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-500">{error}</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100">
      <div className="max-w-6xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold text-gray-900 mb-6">Transactions</h1>

        {/* Filters */}
        <div className="bg-white rounded-lg shadow p-4 mb-6">
          <div className="flex gap-4">
            <div className="flex-1">
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Filter by Ticker
              </label>
              <input
                type="text"
                value={filter.ticker}
                onChange={(e) => setFilter((prev) => ({ ...prev, ticker: e.target.value }))}
                placeholder="e.g., VWRA"
                className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div className="w-40">
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Type
              </label>
              <select
                value={filter.type}
                onChange={(e) => setFilter((prev) => ({ ...prev, type: e.target.value }))}
                className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="">All</option>
                <option value="1">Buy</option>
                <option value="2">Sell</option>
              </select>
            </div>
          </div>
        </div>

        {/* Summary */}
        <div className="mb-4 text-sm text-gray-600">
          Showing {filteredTransactions.length} of {transactions.length} transactions
        </div>

        {/* Transaction List */}
        <div className="bg-white rounded-lg shadow">
          <TransactionList
            transactions={filteredTransactions}
            onDelete={handleDelete}
          />
        </div>
      </div>
    </div>
  );
}
