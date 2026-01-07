import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { ChevronDown } from 'lucide-react';
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
        <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">儀表板</h1>

        {/* Aggregate Summary */}
        <div className="card-dark p-6 mb-8">
          <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">整體投資組合摘要</h2>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="metric-card metric-card-cream">
              <p className="text-[var(--text-muted)] text-sm mb-1">總成本</p>
              <p className="text-2xl font-bold text-[var(--accent-cream)] number-display">
                {formatNumber(aggregateTotals.totalCost)}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card metric-card-sand">
              <p className="text-[var(--text-muted)] text-sm mb-1">目前市值</p>
              <p className="text-2xl font-bold text-[var(--accent-sand)] number-display">
                {aggregateTotals.hasValue ? formatNumber(aggregateTotals.totalValue) : '-'}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card metric-card-peach">
              <p className="text-[var(--text-muted)] text-sm mb-1">未實現損益</p>
              <p className={`text-2xl font-bold number-display ${aggregateTotals.totalPnl >= 0 ? 'number-positive' : 'number-negative'}`}>
                {aggregateTotals.hasValue ? formatNumber(aggregateTotals.totalPnl) : '-'}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card metric-card-blush">
              <p className="text-[var(--text-muted)] text-sm mb-1">持倉數量</p>
              <p className="text-2xl font-bold text-[var(--accent-blush)] number-display">
                {aggregateTotals.positionCount}
              </p>
            </div>
          </div>
        </div>

        {/* Portfolio List */}
        {portfolios.length === 0 ? (
          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)] text-lg mb-4">尚無投資組合</p>
            <Link
              to="/portfolios"
              className="btn-accent inline-block"
            >
              建立投資組合
            </Link>
          </div>
        ) : (
          <div className="space-y-4">
            {portfolios.map((portfolio) => {
              const metrics = portfolioMetrics.get(portfolio.id);
              const isSelected = selectedPortfolioId === portfolio.id;

              return (
                <div key={portfolio.id} className="card-dark overflow-hidden">
                  <div
                    className="px-6 py-5 flex justify-between items-center cursor-pointer hover:bg-[var(--bg-hover)] transition-colors"
                    onClick={() => setSelectedPortfolioId(isSelected ? null : portfolio.id)}
                  >
                    <div>
                      <h3 className="text-lg font-semibold text-[var(--text-primary)]">{portfolio.name}</h3>
                      <p className="text-base text-[var(--text-muted)] mt-1">
                        {portfolio.baseCurrency} → {portfolio.homeCurrency}
                      </p>
                    </div>
                    <div className="flex items-center gap-4">
                      <Link
                        to={`/portfolio/${portfolio.id}`}
                        className="text-[var(--accent-peach)] hover:underline text-base"
                        onClick={(e) => e.stopPropagation()}
                      >
                        查看詳情 →
                      </Link>
                      <ChevronDown
                        className={`w-6 h-6 text-[var(--text-muted)] transform transition-transform ${isSelected ? 'rotate-180' : ''}`}
                      />
                    </div>
                  </div>

                  {isSelected && metrics?.summary && (
                    <div className="p-6 border-t border-[var(--border-color)] space-y-6">
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
                    <div className="p-6 border-t border-[var(--border-color)]">
                      <p className="text-[var(--color-danger)] text-base">{metrics.error}</p>
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
