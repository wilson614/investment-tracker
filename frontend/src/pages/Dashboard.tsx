import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { portfolioApi } from '../services/api';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { CurrentPriceInput } from '../components/portfolio/CurrentPriceInput';
import type { Portfolio, PortfolioSummary, XirrResult, CurrentPriceInfo } from '../types';

interface PortfolioWithMetrics {
  portfolio: Portfolio;
  summary: PortfolioSummary | null;
  xirrResult: XirrResult | null;
  isLoading: boolean;
  error: string | null;
}

export function DashboardPage() {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [portfolioMetrics, setPortfolioMetrics] = useState<Map<string, PortfolioWithMetrics>>(new Map());
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null);

  useEffect(() => {
    loadPortfolios();
  }, []);

  const loadPortfolios = async () => {
    try {
      setIsLoading(true);
      setError(null);
      const data = await portfolioApi.getAll();
      setPortfolios(data);

      // Load summaries for all portfolios
      const metricsMap = new Map<string, PortfolioWithMetrics>();
      await Promise.all(
        data.map(async (portfolio) => {
          try {
            const summary = await portfolioApi.getSummary(portfolio.id);
            metricsMap.set(portfolio.id, {
              portfolio,
              summary,
              xirrResult: null,
              isLoading: false,
              error: null,
            });
          } catch (err) {
            metricsMap.set(portfolio.id, {
              portfolio,
              summary: null,
              xirrResult: null,
              isLoading: false,
              error: err instanceof Error ? err.message : 'Failed to load',
            });
          }
        })
      );
      setPortfolioMetrics(metricsMap);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load portfolios');
    } finally {
      setIsLoading(false);
    }
  };

  const handlePricesChange = async (portfolioId: string, prices: Record<string, CurrentPriceInfo>) => {
    const metrics = portfolioMetrics.get(portfolioId);
    if (!metrics) return;

    setPortfolioMetrics((prev) => {
      const newMap = new Map(prev);
      newMap.set(portfolioId, { ...metrics, isLoading: true });
      return newMap;
    });

    try {
      const [summary, xirrResult] = await Promise.all([
        portfolioApi.getSummary(portfolioId, prices),
        portfolioApi.calculateXirr(portfolioId, { currentPrices: prices }),
      ]);

      setPortfolioMetrics((prev) => {
        const newMap = new Map(prev);
        newMap.set(portfolioId, {
          ...metrics,
          summary,
          xirrResult,
          isLoading: false,
          error: null,
        });
        return newMap;
      });
    } catch (err) {
      setPortfolioMetrics((prev) => {
        const newMap = new Map(prev);
        newMap.set(portfolioId, {
          ...metrics,
          isLoading: false,
          error: err instanceof Error ? err.message : 'Failed to calculate',
        });
        return newMap;
      });
    }
  };

  const formatNumber = (value: number | null | undefined) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  };

  // Calculate aggregate totals
  const aggregateTotals = Array.from(portfolioMetrics.values()).reduce(
    (acc, metrics) => {
      if (metrics.summary) {
        acc.totalCost += metrics.summary.totalCostHome;
        if (metrics.summary.totalValueHome != null) {
          acc.totalValue += metrics.summary.totalValueHome;
          acc.hasValue = true;
        }
        if (metrics.summary.totalUnrealizedPnlHome != null) {
          acc.totalPnl += metrics.summary.totalUnrealizedPnlHome;
        }
        acc.positionCount += metrics.summary.positions.length;
      }
      return acc;
    },
    { totalCost: 0, totalValue: 0, totalPnl: 0, positionCount: 0, hasValue: false }
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-gray-500">載入中...</div>
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
        <h1 className="text-2xl font-bold text-gray-900 mb-6">儀表板</h1>

        {/* Aggregate Summary */}
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <h2 className="text-xl font-bold text-gray-900 mb-4">整體投資組合摘要</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="p-4 bg-blue-50 rounded-lg">
              <p className="text-sm text-gray-600">總成本</p>
              <p className="text-lg font-bold text-gray-900">
                {formatNumber(aggregateTotals.totalCost)} TWD
              </p>
            </div>
            <div className="p-4 bg-blue-50 rounded-lg">
              <p className="text-sm text-gray-600">目前市值</p>
              <p className="text-lg font-bold text-gray-900">
                {aggregateTotals.hasValue ? `${formatNumber(aggregateTotals.totalValue)} TWD` : '-'}
              </p>
            </div>
            <div className="p-4 bg-blue-50 rounded-lg">
              <p className="text-sm text-gray-600">未實現損益</p>
              <p className={`text-lg font-bold ${aggregateTotals.totalPnl >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                {aggregateTotals.hasValue ? `${formatNumber(aggregateTotals.totalPnl)} TWD` : '-'}
              </p>
            </div>
            <div className="p-4 bg-blue-50 rounded-lg">
              <p className="text-sm text-gray-600">持倉數量</p>
              <p className="text-lg font-bold text-gray-900">{aggregateTotals.positionCount}</p>
            </div>
          </div>
        </div>

        {/* Portfolio List */}
        {portfolios.length === 0 ? (
          <div className="bg-white rounded-lg shadow p-6 text-center">
            <p className="text-gray-500 mb-4">尚無投資組合</p>
            <Link
              to="/portfolios"
              className="inline-block px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
            >
              建立投資組合
            </Link>
          </div>
        ) : (
          <div className="space-y-6">
            {portfolios.map((portfolio) => {
              const metrics = portfolioMetrics.get(portfolio.id);
              const isSelected = selectedPortfolioId === portfolio.id;

              return (
                <div key={portfolio.id} className="bg-white rounded-lg shadow overflow-hidden">
                  <div
                    className="px-6 py-4 border-b border-gray-200 flex justify-between items-center cursor-pointer hover:bg-gray-50"
                    onClick={() => setSelectedPortfolioId(isSelected ? null : portfolio.id)}
                  >
                    <div>
                      <h3 className="text-lg font-semibold text-gray-900">{portfolio.name}</h3>
                      <p className="text-sm text-gray-500">
                        {portfolio.baseCurrency} → {portfolio.homeCurrency}
                      </p>
                    </div>
                    <div className="flex items-center gap-4">
                      <Link
                        to={`/portfolio/${portfolio.id}`}
                        className="text-blue-600 hover:text-blue-800 text-sm"
                        onClick={(e) => e.stopPropagation()}
                      >
                        查看詳情 →
                      </Link>
                      <svg
                        className={`w-5 h-5 text-gray-500 transform transition-transform ${isSelected ? 'rotate-180' : ''}`}
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                      >
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                      </svg>
                    </div>
                  </div>

                  {isSelected && metrics?.summary && (
                    <div className="p-6 space-y-4">
                      <CurrentPriceInput
                        positions={metrics.summary.positions}
                        onPricesChange={(prices) => handlePricesChange(portfolio.id, prices)}
                        baseCurrency={portfolio.baseCurrency}
                        homeCurrency={portfolio.homeCurrency}
                      />
                      <PerformanceMetrics
                        summary={metrics.summary}
                        xirrResult={metrics.xirrResult}
                        homeCurrency={portfolio.homeCurrency}
                        isLoading={metrics.isLoading}
                      />
                    </div>
                  )}

                  {isSelected && metrics?.error && (
                    <div className="p-6">
                      <p className="text-red-500">{metrics.error}</p>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
