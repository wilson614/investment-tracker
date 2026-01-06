import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { portfolioApi, transactionApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import type { PortfolioSummary, StockTransaction, CreateStockTransactionRequest } from '../types';

export function PortfolioPage() {
  const { id } = useParams<{ id: string }>();
  const [summary, setSummary] = useState<PortfolioSummary | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  const loadData = useCallback(async () => {
    if (!id) return;

    try {
      setIsLoading(true);
      setError(null);

      const [summaryData, transactionsData] = await Promise.all([
        portfolioApi.getSummary(id),
        transactionApi.getByPortfolio(id),
      ]);

      setSummary(summaryData);
      setTransactions(transactionsData);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load portfolio');
    } finally {
      setIsLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleAddTransaction = async (data: CreateStockTransactionRequest) => {
    await transactionApi.create(data);
    await loadData();
    setShowForm(false);
  };

  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('Are you sure you want to delete this transaction?')) {
      return;
    }
    await transactionApi.delete(transactionId);
    await loadData();
  };

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

  if (!summary) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-gray-500">Portfolio not found</div>
      </div>
    );
  }

  const formatNumber = (value: number | null | undefined) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  };

  return (
    <div className="min-h-screen bg-gray-100">
      <div className="max-w-6xl mx-auto px-4 py-8">
        {/* Header */}
        <div className="mb-6">
          <h1 className="text-2xl font-bold text-gray-900">
            {summary.portfolio.name}
          </h1>
          {summary.portfolio.description && (
            <p className="text-gray-600 mt-1">{summary.portfolio.description}</p>
          )}
          <p className="text-sm text-gray-500 mt-1">
            {summary.portfolio.baseCurrency} → {summary.portfolio.homeCurrency}
          </p>
        </div>

        {/* Summary Card */}
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <h2 className="text-lg font-semibold text-gray-800 mb-4">
            Portfolio Summary
          </h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div>
              <div className="text-sm text-gray-600">Total Cost</div>
              <div className="text-xl font-bold text-gray-900">
                {formatNumber(summary.totalCostHome)} {summary.portfolio.homeCurrency}
              </div>
            </div>
            <div>
              <div className="text-sm text-gray-600">Positions</div>
              <div className="text-xl font-bold text-gray-900">
                {summary.positions.length}
              </div>
            </div>
            {summary.totalValueHome !== undefined && (
              <>
                <div>
                  <div className="text-sm text-gray-600">Current Value</div>
                  <div className="text-xl font-bold text-gray-900">
                    {formatNumber(summary.totalValueHome)} {summary.portfolio.homeCurrency}
                  </div>
                </div>
                <div>
                  <div className="text-sm text-gray-600">Unrealized P&L</div>
                  <div
                    className={`text-xl font-bold ${
                      (summary.totalUnrealizedPnlHome ?? 0) >= 0
                        ? 'text-green-600'
                        : 'text-red-600'
                    }`}
                  >
                    {formatNumber(summary.totalUnrealizedPnlHome ?? 0)}{' '}
                    {summary.portfolio.homeCurrency}
                  </div>
                </div>
              </>
            )}
          </div>
        </div>

        {/* Positions */}
        {summary.positions.length > 0 && (
          <div className="mb-6">
            <h2 className="text-lg font-semibold text-gray-800 mb-4">
              Positions
            </h2>
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {summary.positions.map((position) => (
                <PositionCard
                  key={position.ticker}
                  position={position}
                  homeCurrency={summary.portfolio.homeCurrency}
                />
              ))}
            </div>
          </div>
        )}

        {/* Transaction Form */}
        <div className="mb-6">
          {showForm ? (
            <TransactionForm
              portfolioId={id!}
              onSubmit={handleAddTransaction}
              onCancel={() => setShowForm(false)}
            />
          ) : (
            <button
              onClick={() => setShowForm(true)}
              className="w-full py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 font-medium"
            >
              + Add Transaction
            </button>
          )}
        </div>

        {/* Transaction List */}
        <div className="bg-white rounded-lg shadow">
          <div className="px-4 py-3 border-b border-gray-200">
            <h2 className="text-lg font-semibold text-gray-800">Transactions</h2>
          </div>
          <TransactionList
            transactions={transactions}
            onDelete={handleDeleteTransaction}
          />
        </div>
      </div>
    </div>
  );
}
