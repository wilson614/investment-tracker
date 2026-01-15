import { useState, useEffect } from 'react';
import { Loader2, TrendingUp, TrendingDown, Calendar } from 'lucide-react';
import { portfolioApi } from '../services/api';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { YearSelector } from '../components/performance/YearSelector';
import { MissingPriceModal } from '../components/modals/MissingPriceModal';
import { PerformanceBarChart } from '../components/charts';
import type { Portfolio, YearEndPriceInfo } from '../types';

export function PerformancePage() {
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [isLoadingPortfolio, setIsLoadingPortfolio] = useState(true);
  const [showMissingPriceModal, setShowMissingPriceModal] = useState(false);

  const {
    availableYears,
    selectedYear,
    performance,
    isLoadingYears,
    isLoadingPerformance,
    error,
    setSelectedYear,
    calculatePerformance,
  } = useHistoricalPerformance({
    portfolioId: portfolio?.id,
    autoFetch: true,
  });

  useEffect(() => {
    const loadPortfolio = async () => {
      try {
        setIsLoadingPortfolio(true);
        const portfolios = await portfolioApi.getAll();
        if (portfolios.length > 0) {
          setPortfolio(portfolios[0]);
        }
      } catch (err) {
        console.error('Failed to load portfolio:', err);
      } finally {
        setIsLoadingPortfolio(false);
      }
    };

    loadPortfolio();
  }, []);

  const handleYearChange = (year: number) => {
    setSelectedYear(year);
  };

  const handleMissingPricesSubmit = (prices: Record<string, YearEndPriceInfo>) => {
    if (selectedYear) {
      calculatePerformance(selectedYear, prices);
    }
    setShowMissingPriceModal(false);
  };

  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  const formatCurrency = (value: number | null | undefined) => {
    if (value == null) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  if (isLoadingPortfolio) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--accent-peach)]" />
      </div>
    );
  }

  if (!portfolio) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)]">找不到投資組合</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Header */}
        <div className="flex justify-between items-center mb-6">
          <div>
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">歷史績效</h1>
            <p className="text-[var(--text-secondary)] text-sm mt-1">
              查看投資組合的年度績效表現
            </p>
          </div>

          <div className="flex items-center gap-4">
            <YearSelector
              years={availableYears?.years ?? []}
              selectedYear={selectedYear}
              currentYear={availableYears?.currentYear ?? new Date().getFullYear()}
              onChange={handleYearChange}
              isLoading={isLoadingYears}
            />
          </div>
        </div>

        {error && (
          <div className="card-dark p-4 mb-6 border-l-4 border-[var(--color-danger)]">
            <p className="text-[var(--color-danger)]">{error}</p>
          </div>
        )}

        {/* Performance Card */}
        {isLoadingPerformance ? (
          <div className="card-dark p-8 flex items-center justify-center">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
            <span className="ml-2 text-[var(--text-muted)]">計算績效中...</span>
          </div>
        ) : performance ? (
          <>
            {/* Missing Prices Warning */}
            {performance.missingPrices.length > 0 && (
              <div className="card-dark p-4 mb-6 border-l-4 border-[var(--color-warning)]">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-[var(--color-warning)] font-medium">
                      缺少 {performance.missingPrices.length} 支股票的年底價格
                    </p>
                    <p className="text-[var(--text-muted)] text-sm mt-1">
                      需要提供價格資料才能計算完整績效
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setShowMissingPriceModal(true)}
                    className="btn-accent px-4 py-2"
                  >
                    輸入價格
                  </button>
                </div>
              </div>
            )}

            {/* Performance Metrics */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
              {/* XIRR Card */}
              <div className="card-dark p-6">
                <div className="flex items-center gap-2 mb-2">
                  <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                  <h3 className="text-[var(--text-muted)]">
                    {selectedYear} 年度 XIRR
                  </h3>
                </div>
                {performance.xirrPercentage != null ? (
                  <div className="flex items-baseline gap-2">
                    {performance.xirrPercentage >= 0 ? (
                      <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                    ) : (
                      <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                    )}
                    <span className={`text-3xl font-bold number-display ${
                      performance.xirrPercentage >= 0 ? 'number-positive' : 'number-negative'
                    }`}>
                      {formatPercent(performance.xirrPercentage)}
                    </span>
                  </div>
                ) : (
                  <span className="text-2xl text-[var(--text-muted)]">-</span>
                )}
                <p className="text-xs text-[var(--text-muted)] mt-2">
                  {performance.cashFlowCount} 筆現金流
                </p>
              </div>

              {/* Total Return Card */}
              <div className="card-dark p-6">
                <div className="flex items-center gap-2 mb-2">
                  <TrendingUp className="w-5 h-5 text-[var(--accent-butter)]" />
                  <h3 className="text-[var(--text-muted)]">總報酬率</h3>
                </div>
                {performance.totalReturnPercentage != null ? (
                  <span className={`text-3xl font-bold number-display ${
                    performance.totalReturnPercentage >= 0 ? 'number-positive' : 'number-negative'
                  }`}>
                    {formatPercent(performance.totalReturnPercentage)}
                  </span>
                ) : (
                  <span className="text-2xl text-[var(--text-muted)]">-</span>
                )}
              </div>
            </div>

            {/* Value Summary */}
            <div className="card-dark p-6">
              <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                {selectedYear} 年度摘要
              </h3>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <div>
                  <p className="text-sm text-[var(--text-muted)]">年初價值</p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {formatCurrency(performance.startValueHome)} {portfolio.homeCurrency}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">年底價值</p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {formatCurrency(performance.endValueHome)} {portfolio.homeCurrency}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">淨投入</p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {formatCurrency(performance.netContributionsHome)} {portfolio.homeCurrency}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">淨獲利</p>
                  <p className={`text-lg font-medium number-display ${
                    (performance.endValueHome ?? 0) - (performance.startValueHome ?? 0) - performance.netContributionsHome >= 0
                      ? 'number-positive'
                      : 'number-negative'
                  }`}>
                    {formatCurrency(
                      (performance.endValueHome ?? 0) -
                      (performance.startValueHome ?? 0) -
                      performance.netContributionsHome
                    )} {portfolio.homeCurrency}
                  </p>
                </div>
              </div>
            </div>

            {/* Performance Bar Chart */}
            {performance.xirrPercentage != null && performance.totalReturnPercentage != null && (
              <div className="card-dark p-6 mt-6">
                <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                  績效比較圖
                </h3>
                <PerformanceBarChart
                  data={[
                    {
                      label: `${selectedYear} XIRR`,
                      value: performance.xirrPercentage,
                      tooltip: `年化報酬率 (${performance.cashFlowCount} 筆現金流)`,
                    },
                    {
                      label: `${selectedYear} 總報酬率`,
                      value: performance.totalReturnPercentage,
                      tooltip: '總報酬率 (含股息)',
                    },
                  ]}
                  height={120}
                />
              </div>
            )}
          </>
        ) : selectedYear ? (
          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)]">選擇年份以查看績效</p>
          </div>
        ) : (
          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)]">無交易資料</p>
          </div>
        )}

        {/* Missing Price Modal */}
        {performance && (
          <MissingPriceModal
            isOpen={showMissingPriceModal}
            onClose={() => setShowMissingPriceModal(false)}
            missingPrices={performance.missingPrices}
            year={selectedYear ?? new Date().getFullYear()}
            onSubmit={handleMissingPricesSubmit}
          />
        )}
      </div>
    </div>
  );
}

export default PerformancePage;
