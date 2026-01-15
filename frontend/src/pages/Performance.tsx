import { useState, useEffect, useCallback, useRef } from 'react';
import { Loader2, TrendingUp, TrendingDown, Calendar, RefreshCw } from 'lucide-react';
import { portfolioApi, stockPriceApi, marketDataApi } from '../services/api';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { YearSelector } from '../components/performance/YearSelector';
import { MissingPriceModal } from '../components/modals/MissingPriceModal';
import { PerformanceBarChart } from '../components/charts';
import { StockMarket } from '../types';
import { getEuronextSymbol } from '../constants';
import type { Portfolio, YearEndPriceInfo, StockMarket as StockMarketType, MissingPrice, MarketYtdComparison } from '../types';

// Available benchmark options for comparison
const BENCHMARK_OPTIONS = [
  { key: 'All Country', label: '全球 (VWRA)', symbol: 'VWRA' },
  { key: 'US Large', label: '美國大型 (VUAA)', symbol: 'VUAA' },
  { key: 'Developed Markets Large', label: '已開發大型 (VHVE)', symbol: 'VHVE' },
  { key: 'Emerging Markets', label: '新興市場 (VFEM)', symbol: 'VFEM' },
  { key: 'Taiwan 0050', label: '台灣 0050', symbol: '0050' },
] as const;

// Cache key for localStorage quote cache (shared with Portfolio page)
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

const guessMarket = (ticker: string): StockMarketType => {
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  return StockMarket.US;
};

// Check if a ticker is Taiwan market (uses Sina, not Stooq)
const isTaiwanTicker = (ticker: string): boolean => {
  return /^\d+[A-Za-z]*$/.test(ticker);
};

export function PerformancePage() {
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [isLoadingPortfolio, setIsLoadingPortfolio] = useState(true);
  const [showMissingPriceModal, setShowMissingPriceModal] = useState(false);
  const [isFetchingPrices, setIsFetchingPrices] = useState(false);
  const hasFetchedForYearRef = useRef<number | null>(null);

  // Benchmark comparison state
  const [selectedBenchmark, setSelectedBenchmark] = useState<string>('All Country');
  const [benchmarkReturn, setBenchmarkReturn] = useState<number | null>(null);
  const [isLoadingBenchmark, setIsLoadingBenchmark] = useState(false);
  const [ytdData, setYtdData] = useState<MarketYtdComparison | null>(null);

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

  // Load YTD benchmark data for current year comparison
  useEffect(() => {
    const loadYtdData = async () => {
      try {
        const data = await marketDataApi.getYtdComparison();
        setYtdData(data);
      } catch (err) {
        console.error('Failed to load YTD data:', err);
      }
    };
    loadYtdData();
  }, []);

  // Calculate benchmark return when year or benchmark changes
  useEffect(() => {
    const fetchBenchmarkReturn = async () => {
      if (!selectedYear || !availableYears) return;

      setIsLoadingBenchmark(true);
      setBenchmarkReturn(null);

      try {
        const isCurrentYear = selectedYear === availableYears.currentYear;

        if (isCurrentYear && ytdData) {
          // Use YTD data for current year
          const benchmark = ytdData.benchmarks.find(b => b.marketKey === selectedBenchmark);
          if (benchmark?.ytdReturnPercent != null) {
            setBenchmarkReturn(benchmark.ytdReturnPercent);
          }
        } else {
          // For historical years, fetch from Stooq via index prices
          const benchmarkInfo = BENCHMARK_OPTIONS.find(b => b.key === selectedBenchmark);
          if (!benchmarkInfo) return;

          // Get year-start (prior year Dec) and year-end prices
          const yearStartDate = `${selectedYear - 1}-12-31`;
          const yearEndDate = `${selectedYear}-12-31`;

          const [startResult, endResult] = await Promise.all([
            marketDataApi.getHistoricalPrice(benchmarkInfo.symbol, yearStartDate).catch(() => null),
            marketDataApi.getHistoricalPrice(benchmarkInfo.symbol, yearEndDate).catch(() => null),
          ]);

          if (startResult && endResult && startResult.price > 0) {
            const returnPercent = ((endResult.price - startResult.price) / startResult.price) * 100;
            setBenchmarkReturn(returnPercent);
          }
        }
      } catch (err) {
        console.error('Failed to fetch benchmark return:', err);
      } finally {
        setIsLoadingBenchmark(false);
      }
    };

    fetchBenchmarkReturn();
  }, [selectedYear, selectedBenchmark, availableYears, ytdData]);

  // Load cached prices from localStorage
  const loadCachedPrices = useCallback((tickers: string[]): Record<string, YearEndPriceInfo> => {
    const prices: Record<string, YearEndPriceInfo> = {};
    for (const ticker of tickers) {
      try {
        const cached = localStorage.getItem(getQuoteCacheKey(ticker));
        if (cached) {
          const data = JSON.parse(cached);
          if (data.quote?.price && data.quote?.exchangeRate) {
            prices[ticker] = {
              price: data.quote.price,
              exchangeRate: data.quote.exchangeRate,
            };
          }
        }
      } catch {
        // Ignore cache errors
      }
    }
    return prices;
  }, []);

  // Fetch prices for missing tickers (for YTD - uses real-time Sina/Euronext API)
  const fetchCurrentPrices = useCallback(async (
    missingPrices: MissingPrice[],
    homeCurrency: string
  ): Promise<Record<string, YearEndPriceInfo>> => {
    const prices: Record<string, YearEndPriceInfo> = {};

    await Promise.all(missingPrices.map(async (mp) => {
      try {
        // Check if this is a Euronext symbol first
        const euronextInfo = getEuronextSymbol(mp.ticker);
        if (euronextInfo) {
          const euronextQuote = await marketDataApi.getEuronextQuote(
            euronextInfo.isin,
            euronextInfo.mic,
            homeCurrency
          );
          if (euronextQuote?.exchangeRate) {
            prices[mp.ticker] = {
              price: euronextQuote.price,
              exchangeRate: euronextQuote.exchangeRate,
            };
          }
          return;
        }

        // Standard market handling
        const market = guessMarket(mp.ticker);
        let quote = await stockPriceApi.getQuoteWithRate(market, mp.ticker, homeCurrency);

        if (!quote && market === StockMarket.US) {
          quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, mp.ticker, homeCurrency);
        }

        if (quote?.exchangeRate) {
          prices[mp.ticker] = {
            price: quote.price,
            exchangeRate: quote.exchangeRate,
          };
        }
      } catch {
        // If US fails, try UK as fallback (for ETFs like VWRA)
        if (guessMarket(mp.ticker) === StockMarket.US) {
          try {
            const ukQuote = await stockPriceApi.getQuoteWithRate(
              StockMarket.UK, mp.ticker, homeCurrency
            );
            if (ukQuote?.exchangeRate) {
              prices[mp.ticker] = {
                price: ukQuote.price,
                exchangeRate: ukQuote.exchangeRate,
              };
            }
          } catch {
            // UK also failed
            console.error(`Failed to fetch price for ${mp.ticker} from both US and UK markets`);
          }
        } else {
          console.error(`Failed to fetch price for ${mp.ticker}`);
        }
      }
    }));

    return prices;
  }, []);

  // Fetch historical prices (for past years - uses Stooq API)
  // Returns separate year-start and year-end prices with real historical exchange rates
  const fetchHistoricalPrices = useCallback(async (
    missingPrices: MissingPrice[],
    year: number,
    homeCurrency: string
  ): Promise<{ yearStartPrices: Record<string, YearEndPriceInfo>; yearEndPrices: Record<string, YearEndPriceInfo> }> => {
    const yearStartPrices: Record<string, YearEndPriceInfo> = {};
    const yearEndPrices: Record<string, YearEndPriceInfo> = {};

    // Separate by price type
    const yearStartMissing = missingPrices.filter(mp => mp.priceType === 'YearStart');
    const yearEndMissing = missingPrices.filter(mp => mp.priceType === 'YearEnd');

    // Separate Taiwan tickers (use Sina) from international tickers (use Stooq)
    const intlYearStartTickers = yearStartMissing.filter(mp => !isTaiwanTicker(mp.ticker));
    const intlYearEndTickers = yearEndMissing.filter(mp => !isTaiwanTicker(mp.ticker));
    const taiwanTickers = missingPrices.filter(mp => isTaiwanTicker(mp.ticker));

    // For Taiwan tickers, we still need real-time API (Stooq doesn't support Taiwan)
    // These will remain as "missing" and user needs to input manually
    if (taiwanTickers.length > 0) {
      console.log(`Taiwan tickers ${taiwanTickers.map(t => t.ticker).join(', ')} require manual input for historical prices`);
    }

    // Collect unique currencies needed for exchange rate lookup
    const currenciesNeeded = new Set<string>();

    // Fetch year-start prices from prior year Dec 31
    const yearStartDate = `${year - 1}-12-31`;
    if (intlYearStartTickers.length > 0) {
      const tickers = intlYearStartTickers.map(mp => mp.ticker);

      try {
        const stooqPrices = await marketDataApi.getHistoricalPrices(tickers, yearStartDate);

        for (const mp of intlYearStartTickers) {
          const result = stooqPrices[mp.ticker];
          if (result && result.currency !== homeCurrency) {
            currenciesNeeded.add(result.currency);
          }
        }

        // Fetch historical exchange rates for year-start
        const yearStartRates: Record<string, number> = {};
        if (currenciesNeeded.size > 0 && homeCurrency === 'TWD') {
          const ratePromises = Array.from(currenciesNeeded).map(async (currency) => {
            try {
              const rate = await marketDataApi.getHistoricalExchangeRate(currency, homeCurrency, yearStartDate);
              if (rate) {
                yearStartRates[currency] = rate.rate;
              }
            } catch {
              console.warn(`Failed to fetch ${currency}/${homeCurrency} rate for ${yearStartDate}`);
            }
          });
          await Promise.all(ratePromises);
        }

        for (const mp of intlYearStartTickers) {
          const result = stooqPrices[mp.ticker];
          if (result) {
            let exchangeRate: number;
            if (result.currency === homeCurrency) {
              exchangeRate = 1;
            } else if (yearStartRates[result.currency]) {
              exchangeRate = yearStartRates[result.currency];
            } else {
              // Fallback to hardcoded rate if API fails
              exchangeRate = getFallbackExchangeRate(result.currency, homeCurrency) ?? 1;
            }
            yearStartPrices[mp.ticker] = {
              price: result.price,
              exchangeRate,
            };
          }
        }
      } catch (err) {
        console.error('Failed to fetch year-start prices from Stooq:', err);
      }
    }

    // Fetch year-end prices from current year Dec 31
    const yearEndDate = `${year}-12-31`;
    if (intlYearEndTickers.length > 0) {
      const tickers = intlYearEndTickers.map(mp => mp.ticker);
      const yearEndCurrenciesNeeded = new Set<string>();

      try {
        const stooqPrices = await marketDataApi.getHistoricalPrices(tickers, yearEndDate);

        for (const mp of intlYearEndTickers) {
          const result = stooqPrices[mp.ticker];
          if (result && result.currency !== homeCurrency) {
            yearEndCurrenciesNeeded.add(result.currency);
          }
        }

        // Fetch historical exchange rates for year-end
        const yearEndRates: Record<string, number> = {};
        if (yearEndCurrenciesNeeded.size > 0 && homeCurrency === 'TWD') {
          const ratePromises = Array.from(yearEndCurrenciesNeeded).map(async (currency) => {
            try {
              const rate = await marketDataApi.getHistoricalExchangeRate(currency, homeCurrency, yearEndDate);
              if (rate) {
                yearEndRates[currency] = rate.rate;
              }
            } catch {
              console.warn(`Failed to fetch ${currency}/${homeCurrency} rate for ${yearEndDate}`);
            }
          });
          await Promise.all(ratePromises);
        }

        for (const mp of intlYearEndTickers) {
          const result = stooqPrices[mp.ticker];
          if (result) {
            let exchangeRate: number;
            if (result.currency === homeCurrency) {
              exchangeRate = 1;
            } else if (yearEndRates[result.currency]) {
              exchangeRate = yearEndRates[result.currency];
            } else {
              // Fallback to hardcoded rate if API fails
              exchangeRate = getFallbackExchangeRate(result.currency, homeCurrency) ?? 1;
            }
            yearEndPrices[mp.ticker] = {
              price: result.price,
              exchangeRate,
            };
          }
        }
      } catch (err) {
        console.error('Failed to fetch year-end prices from Stooq:', err);
      }
    }

    return { yearStartPrices, yearEndPrices };
  }, []);

  // Fallback exchange rates (used only when API fails)
  const getFallbackExchangeRate = (currency: string, homeCurrency: string): number | null => {
    if (currency === homeCurrency) return 1;

    if (homeCurrency === 'TWD') {
      const toTwd: Record<string, number> = {
        'USD': 32,
        'GBP': 40,
        'EUR': 35,
        'JPY': 0.21,
      };
      return toTwd[currency] || null;
    }

    if (homeCurrency === 'USD') {
      const toUsd: Record<string, number> = {
        'GBP': 1.25,
        'EUR': 1.08,
        'JPY': 0.0067,
        'TWD': 0.031,
      };
      return toUsd[currency] || null;
    }

    return null;
  };

  // Auto-fetch prices when we have missing prices (uses different API based on year)
  useEffect(() => {
    const autoFetchPrices = async () => {
      if (!performance || !portfolio || !selectedYear || !availableYears) return;
      if (performance.missingPrices.length === 0) return;
      if (hasFetchedForYearRef.current === selectedYear) return; // Already fetched for this year

      hasFetchedForYearRef.current = selectedYear;
      setIsFetchingPrices(true);

      try {
        const isCurrentYear = selectedYear === availableYears.currentYear;

        if (isCurrentYear) {
          // YTD: Use cached prices first, then fetch remaining from Sina/Euronext
          const tickers = performance.missingPrices.map(mp => mp.ticker);
          const cachedPrices = loadCachedPrices(tickers);

          const stillMissing = performance.missingPrices.filter(
            mp => !cachedPrices[mp.ticker]
          );

          let fetchedPrices: Record<string, YearEndPriceInfo> = {};
          if (stillMissing.length > 0) {
            fetchedPrices = await fetchCurrentPrices(stillMissing, portfolio.homeCurrency);
          }

          const allPrices = { ...cachedPrices, ...fetchedPrices };
          if (Object.keys(allPrices).length > 0) {
            calculatePerformance(selectedYear, allPrices);
          }
        } else {
          // Historical year: Use Stooq for international stocks
          const { yearStartPrices, yearEndPrices } = await fetchHistoricalPrices(
            performance.missingPrices,
            selectedYear,
            portfolio.homeCurrency
          );

          const hasPrices = Object.keys(yearEndPrices).length > 0 || Object.keys(yearStartPrices).length > 0;
          if (hasPrices) {
            calculatePerformance(selectedYear, yearEndPrices, yearStartPrices);
          }
        }
      } catch (err) {
        console.error('Failed to auto-fetch prices:', err);
      } finally {
        setIsFetchingPrices(false);
      }
    };

    autoFetchPrices();
  }, [performance, portfolio, selectedYear, availableYears, loadCachedPrices, fetchCurrentPrices, fetchHistoricalPrices, calculatePerformance]);

  // Manual refresh button handler
  const handleRefreshPrices = async () => {
    if (!performance || !portfolio || !selectedYear || !availableYears) return;
    if (performance.missingPrices.length === 0) return;

    setIsFetchingPrices(true);
    hasFetchedForYearRef.current = null; // Reset to allow re-fetch

    try {
      const isCurrentYear = selectedYear === availableYears.currentYear;

      if (isCurrentYear) {
        // YTD: Use Sina/Euronext real-time API
        const fetchedPrices = await fetchCurrentPrices(
          performance.missingPrices,
          portfolio.homeCurrency
        );
        if (Object.keys(fetchedPrices).length > 0) {
          calculatePerformance(selectedYear, fetchedPrices);
        }
      } else {
        // Historical: Use Stooq historical API
        const { yearStartPrices, yearEndPrices } = await fetchHistoricalPrices(
          performance.missingPrices,
          selectedYear,
          portfolio.homeCurrency
        );
        const hasPrices = Object.keys(yearEndPrices).length > 0 || Object.keys(yearStartPrices).length > 0;
        if (hasPrices) {
          calculatePerformance(selectedYear, yearEndPrices, yearStartPrices);
        }
      }
    } catch (err) {
      console.error('Failed to fetch prices:', err);
    } finally {
      setIsFetchingPrices(false);
    }
  };

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
                      缺少 {performance.missingPrices.length} 支股票的價格
                    </p>
                    <p className="text-[var(--text-muted)] text-sm mt-1">
                      {isFetchingPrices
                        ? (selectedYear === availableYears?.currentYear
                          ? '正在抓取即時報價...'
                          : `正在抓取 ${selectedYear} 年底收盤價...`)
                        : (selectedYear === availableYears?.currentYear
                          ? '可自動抓取即時報價'
                          : `可自動抓取 ${selectedYear} 年底收盤價 (國際股票)`
                        )
                      }
                    </p>
                  </div>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={handleRefreshPrices}
                      disabled={isFetchingPrices}
                      className="btn-dark px-3 py-2 flex items-center gap-2"
                    >
                      {isFetchingPrices ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <RefreshCw className="w-4 h-4" />
                      )}
                      抓取價格
                    </button>
                    <button
                      type="button"
                      onClick={() => setShowMissingPriceModal(true)}
                      className="btn-accent px-4 py-2"
                    >
                      手動輸入
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* Performance Metrics - XIRR Cards Only */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
              {/* Source Currency XIRR Card (USD) */}
              {performance.sourceCurrency && performance.xirrPercentageSource != null && (
                <div className="card-dark p-6 border-l-4 border-[var(--accent-peach)]">
                  <div className="flex items-center gap-2 mb-2">
                    <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                    <h3 className="text-[var(--text-muted)]">
                      {selectedYear} 年度 XIRR ({performance.sourceCurrency})
                    </h3>
                  </div>
                  <div className="flex items-baseline gap-2">
                    {performance.xirrPercentageSource >= 0 ? (
                      <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                    ) : (
                      <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                    )}
                    <span className={`text-3xl font-bold number-display ${
                      performance.xirrPercentageSource >= 0 ? 'number-positive' : 'number-negative'
                    }`}>
                      {formatPercent(performance.xirrPercentageSource)}
                    </span>
                  </div>
                  <p className="text-xs text-[var(--text-muted)] mt-2">
                    原幣報酬率（不含匯率變動）
                  </p>
                </div>
              )}

              {/* Home Currency XIRR Card (TWD) */}
              <div className="card-dark p-6">
                <div className="flex items-center gap-2 mb-2">
                  <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                  <h3 className="text-[var(--text-muted)]">
                    {selectedYear} 年度 XIRR ({portfolio.homeCurrency})
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
                  {performance.cashFlowCount} 筆現金流（含匯率變動）
                </p>
              </div>
            </div>

            {/* Value Summary - Source Currency */}
            {performance.sourceCurrency && performance.startValueSource != null && (
              <div className="card-dark p-6 mb-6 border-l-4 border-[var(--accent-peach)]">
                <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                  {selectedYear} 年度摘要 ({performance.sourceCurrency})
                </h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">年初價值</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {formatCurrency(performance.startValueSource)} {performance.sourceCurrency}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">年底價值</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {formatCurrency(performance.endValueSource)} {performance.sourceCurrency}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">淨投入</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {formatCurrency(performance.netContributionsSource)} {performance.sourceCurrency}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">淨獲利</p>
                    <p className={`text-lg font-medium number-display ${
                      (performance.endValueSource ?? 0) - (performance.startValueSource ?? 0) - (performance.netContributionsSource ?? 0) >= 0
                        ? 'number-positive'
                        : 'number-negative'
                    }`}>
                      {formatCurrency(
                        (performance.endValueSource ?? 0) -
                        (performance.startValueSource ?? 0) -
                        (performance.netContributionsSource ?? 0)
                      )} {performance.sourceCurrency}
                    </p>
                  </div>
                </div>
              </div>
            )}

            {/* Value Summary - Home Currency */}
            <div className="card-dark p-6">
              <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                {selectedYear} 年度摘要 ({portfolio.homeCurrency})
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

            {/* Performance Bar Chart - Portfolio vs Benchmark */}
            {performance.xirrPercentageSource != null && (
              <div className="card-dark p-6 mt-6">
                <div className="flex justify-between items-center mb-4">
                  <h3 className="text-lg font-bold text-[var(--text-primary)]">
                    績效比較
                  </h3>
                  <select
                    value={selectedBenchmark}
                    onChange={(e) => setSelectedBenchmark(e.target.value)}
                    className="bg-[var(--bg-secondary)] border border-[var(--border-primary)] rounded-lg px-3 py-1.5 text-sm text-[var(--text-primary)]"
                  >
                    {BENCHMARK_OPTIONS.map((option) => (
                      <option key={option.key} value={option.key}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
                <PerformanceBarChart
                  data={[
                    {
                      label: `我的 XIRR (${performance.sourceCurrency})`,
                      value: performance.xirrPercentageSource,
                      tooltip: `${selectedYear} 年化報酬率 (${performance.cashFlowCount} 筆現金流)`,
                    },
                    {
                      label: BENCHMARK_OPTIONS.find(b => b.key === selectedBenchmark)?.label ?? selectedBenchmark,
                      value: benchmarkReturn ?? 0,
                      tooltip: isLoadingBenchmark
                        ? '載入中...'
                        : benchmarkReturn != null
                          ? `${selectedYear} 年度報酬率`
                          : '無法取得報酬率',
                    },
                  ]}
                  height={120}
                />
                {isLoadingBenchmark && (
                  <p className="text-xs text-[var(--text-muted)] mt-2 flex items-center gap-1">
                    <Loader2 className="w-3 h-3 animate-spin" />
                    載入基準報酬...
                  </p>
                )}
                {!isLoadingBenchmark && benchmarkReturn == null && (
                  <p className="text-xs text-[var(--color-warning)] mt-2">
                    無法取得 {selectedYear} 年 {BENCHMARK_OPTIONS.find(b => b.key === selectedBenchmark)?.label} 的報酬率
                  </p>
                )}
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
