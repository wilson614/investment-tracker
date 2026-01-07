import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { portfolioApi, transactionApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { CurrentPriceInput } from '../components/portfolio/CurrentPriceInput';
import { StockImportButton } from '../components/import';
import type { PortfolioSummary, StockTransaction, CreateStockTransactionRequest, XirrResult, CurrentPriceInfo } from '../types';

export function PortfolioPage() {
  const { id } = useParams<{ id: string }>();
  const [summary, setSummary] = useState<PortfolioSummary | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isCalculating, setIsCalculating] = useState(false);
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

  const handlePricesChange = async (prices: Record<string, CurrentPriceInfo>) => {
    if (!id) return;

    setIsCalculating(true);

    try {
      const [summaryData, xirrData] = await Promise.all([
        portfolioApi.getSummary(id, prices),
        portfolioApi.calculateXirr(id, { currentPrices: prices }),
      ]);
      setSummary(summaryData);
      setXirrResult(xirrData);
    } catch (err) {
      console.error('Failed to calculate performance:', err);
    } finally {
      setIsCalculating(false);
    }
  };

  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) {
      return;
    }
    await transactionApi.delete(transactionId);
    await loadData();
  };

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

  if (!summary) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">找不到投資組合</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">
            {summary.portfolio.name}
          </h1>
          {summary.portfolio.description && (
            <p className="text-[var(--text-secondary)] text-base mt-2">{summary.portfolio.description}</p>
          )}
          <p className="text-base text-[var(--text-muted)] mt-1">
            {summary.portfolio.baseCurrency} → {summary.portfolio.homeCurrency}
          </p>
        </div>

        {/* Current Price Input */}
        {summary.positions.length > 0 && (
          <div className="mb-6">
            <CurrentPriceInput
              positions={summary.positions}
              onPricesChange={handlePricesChange}
              baseCurrency={summary.portfolio.baseCurrency}
              homeCurrency={summary.portfolio.homeCurrency}
            />
          </div>
        )}

        {/* Performance Metrics */}
        <div className="mb-6">
          <PerformanceMetrics
            summary={summary}
            xirrResult={xirrResult}
            homeCurrency={summary.portfolio.homeCurrency}
            isLoading={isCalculating}
          />
        </div>

        {/* Positions */}
        {summary.positions.length > 0 && (
          <div className="mb-6">
            <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4">
              持倉
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
            <div className="flex gap-3">
              <button
                onClick={() => setShowForm(true)}
                className="btn-accent flex-1 py-3"
              >
                + 新增交易
              </button>
              <StockImportButton
                portfolioId={id!}
                onImportComplete={loadData}
              />
            </div>
          )}
        </div>

        {/* Transaction List */}
        <div className="card-dark overflow-hidden">
          <div className="px-5 py-4 border-b border-[var(--border-color)]">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">交易紀錄</h2>
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
