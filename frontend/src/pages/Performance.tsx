/**
 * Performance Page
 *
 * 顯示年度績效與對照基準（benchmark）比較。
 *
 * 主要資料來源：
 * - `useHistoricalPerformance`：取得投資組合在指定年度的績效與缺漏價格清單。
 * - `marketDataApi`：取得 YTD benchmark 比較與歷史 benchmark 報酬。
 * - `stockPriceApi` / `marketDataApi.getEuronextQuote`：在需要即時補價（例如當年度）時，抓取缺漏 ticker 的報價。
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { Loader2, TrendingUp, TrendingDown, Calendar, RefreshCw, Info, Settings, X, Check } from 'lucide-react';
import { stockPriceApi, marketDataApi, userBenchmarkApi, userPreferencesApi } from '../services/api';
import { loadCachedYtdData, getYtdData, transformYtdData } from '../services/ytdApi';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { usePortfolio } from '../contexts/PortfolioContext';
import { YearSelector } from '../components/performance/YearSelector';
import { MissingPriceModal } from '../components/modals/MissingPriceModal';
import { PerformanceBarChart } from '../components/charts';
import { XirrWarningBadge } from '../components/common/XirrWarningBadge';
import { StockMarket } from '../types';
import type { YearEndPriceInfo, StockMarket as StockMarketType, MissingPrice, MarketYtdComparison, UserBenchmark } from '../types';

/**
 * 可選擇的 benchmark 清單（需與 backend `MarketYtdService.Benchmarks` 的 key 對齊）。
 *
 * 注意：
 * - key 使用英文（例如 `All Country`），因為後端 API / dashboard 偏好設定會用這個 key。
 */
const BENCHMARK_OPTIONS = [
  { key: 'All Country', label: '全球 (VWRA)', symbol: 'VWRA' },
  { key: 'US Large', label: '美國大型 (VUAA)', symbol: 'VUAA' },
  { key: 'US Small', label: '美國小型 (XRSU)', symbol: 'XRSU' },
  { key: 'Developed Markets Large', label: '已開發大型 (VHVE)', symbol: 'VHVE' },
  { key: 'Developed Markets Small', label: '已開發小型 (WSML)', symbol: 'WSML' },
  { key: 'Dev ex US Large', label: '已開發非美 (EXUS)', symbol: 'EXUS' },
  { key: 'Emerging Markets', label: '新興市場 (VFEM)', symbol: 'VFEM' },
  { key: 'Europe', label: '歐洲 (VEUA)', symbol: 'VEUA' },
  { key: 'Japan', label: '日本 (VJPA)', symbol: 'VJPA' },
  { key: 'China', label: '中國 (HCHA)', symbol: 'HCHA' },
  { key: 'Taiwan 0050', label: '台灣 (0050)', symbol: '0050' },
] as const;

// Shared localStorage key with dashboard MarketYtdSection
const YTD_PREFS_KEY = 'ytd_benchmark_preferences';

/**
 * 從 localStorage 讀取使用者選擇的 benchmark（與 Dashboard 同步，fallback 用）。
 *
 * Dashboard 會存英文 key（例如 `All Country`），這裡會做基本驗證，避免壞資料造成 UI 異常。
 */
function loadSelectedBenchmarksFromLocalStorage(): string[] {
  try {
    const stored = localStorage.getItem(YTD_PREFS_KEY);
    if (stored) {
      const keys = JSON.parse(stored) as string[];
      if (Array.isArray(keys) && keys.length > 0) {
        // Validate keys: system benchmarks must exist in BENCHMARK_OPTIONS, custom benchmarks start with 'custom_'
        const validKeys = keys.filter(k =>
          BENCHMARK_OPTIONS.some(o => o.key === k) || k.startsWith('custom_')
        );
        if (validKeys.length > 0) return validKeys;
      }
    }
  } catch {
    // Ignore
  }
  return ['All Country']; // Default
}

/**
 * 將使用者選擇的 benchmark 寫回 localStorage（fallback 用）。
 */
function saveSelectedBenchmarksToLocalStorage(keys: string[]): void {
  try {
    localStorage.setItem(YTD_PREFS_KEY, JSON.stringify(keys));
  } catch {
    // Ignore
  }
}

// Cache key for localStorage quote cache (shared with Portfolio page)
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

/**
 * 依 ticker 格式推測市場別。
 *
 * - TW：純數字或數字+英文字尾（例如 `2330`、`00878`、`6547M`）
 * - UK：以 `.L` 結尾
 * - 其他：預設 US
 */
const guessMarket = (ticker: string): StockMarketType => {
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  return StockMarket.US;
};

export function PerformancePage() {
  // Use shared portfolio context (synced with Portfolio page)
  const { currentPortfolio: portfolio, isLoading: isLoadingPortfolio, performanceVersion } = usePortfolio();
  const [showMissingPriceModal, setShowMissingPriceModal] = useState(false);
  const [isFetchingPrices, setIsFetchingPrices] = useState(false);
  const [priceFetchFailed, setPriceFetchFailed] = useState(false);
  const hasFetchedForYearRef = useRef<number | null>(null);
  const fetchRetryCountRef = useRef<number>(0); // Limit auto-retries to prevent infinite loops

  // Benchmark comparison state - multi-select support, synced with dashboard
  const [selectedBenchmarks, setSelectedBenchmarks] = useState<string[]>(loadSelectedBenchmarksFromLocalStorage);
  const [tempSelectedBenchmarks, setTempSelectedBenchmarks] = useState<string[]>([]);
  const [showBenchmarkSettings, setShowBenchmarkSettings] = useState(false);
  const [benchmarkReturns, setBenchmarkReturns] = useState<Record<string, number | null>>({});
  const [isLoadingBenchmark, setIsLoadingBenchmark] = useState(true); // Start as true to prevent flash
  const [ytdData, setYtdData] = useState<MarketYtdComparison | null>(null);
  const lastResetVersionRef = useRef<number>(performanceVersion); // Track version to detect stale state

  // Custom user benchmarks state
  const [customBenchmarks, setCustomBenchmarks] = useState<UserBenchmark[]>([]);
  const [customBenchmarkReturns, setCustomBenchmarkReturns] = useState<Record<string, number | null>>({});

  // Load user preferences from API on mount
  useEffect(() => {
    const loadPreferences = async () => {
      try {
        const prefs = await userPreferencesApi.get();
        if (prefs.ytdBenchmarkPreferences) {
          const benchmarks = JSON.parse(prefs.ytdBenchmarkPreferences) as string[];
          if (Array.isArray(benchmarks) && benchmarks.length > 0) {
            // Validate keys: system benchmarks must exist in BENCHMARK_OPTIONS, custom benchmarks start with 'custom_'
            const validKeys = benchmarks.filter(k =>
              BENCHMARK_OPTIONS.some(o => o.key === k) || k.startsWith('custom_')
            );
            if (validKeys.length > 0) {
              setSelectedBenchmarks(validKeys);
              // Sync to localStorage for offline use
              saveSelectedBenchmarksToLocalStorage(validKeys);
            }
          }
        }
      } catch (err) {
        console.error('Failed to load preferences from API, using localStorage:', err);
        // Keep localStorage values as fallback
      }
    };
    loadPreferences();
  }, []);

  // Save preferences to API
  const savePreferences = useCallback(async (keys: string[]) => {
    // Always save to localStorage first (for immediate sync)
    saveSelectedBenchmarksToLocalStorage(keys);

    // Then save to API
    try {
      await userPreferencesApi.update({
        ytdBenchmarkPreferences: JSON.stringify(keys),
      });
    } catch (err) {
      console.error('Failed to save preferences to API:', err);
    }
  }, []);

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
    version: performanceVersion, // Clear state on portfolio switch
  });

  // Reset price fetch state when portfolio changes
  useEffect(() => {
    hasFetchedForYearRef.current = null;
    fetchRetryCountRef.current = 0;
    setPriceFetchFailed(false);
    setIsFetchingPrices(false);
    // Reset benchmark state to prevent stale data showing
    setBenchmarkReturns({});
    setIsLoadingBenchmark(true);
    // Update version ref to mark state as current
    lastResetVersionRef.current = performanceVersion;
  }, [performanceVersion]);

  // Load YTD benchmark data for current year comparison
  // Use cached data first for instant display, then fetch fresh data in background
  useEffect(() => {
    // First, try to load from cache for instant display
    const cached = loadCachedYtdData();
    if (cached.data) {
      setYtdData(transformYtdData(cached.data));
    }

    // Then fetch fresh data in background
    const loadFreshYtdData = async () => {
      try {
        const data = await getYtdData();
        setYtdData(transformYtdData(data));
      } catch (err) {
        console.error('Failed to load YTD data:', err);
      }
    };
    loadFreshYtdData();
  }, []);

  // Load user's custom benchmarks
  useEffect(() => {
    const loadCustomBenchmarks = async () => {
      try {
        const benchmarks = await userBenchmarkApi.getAll();
        setCustomBenchmarks(benchmarks);

        // 清理已刪除的自訂基準：從 selectedBenchmarks 中移除不存在的 custom_ key
        const validCustomKeys = new Set(benchmarks.map(b => `custom_${b.id}`));
        setSelectedBenchmarks(prev => {
          const cleaned = prev.filter(key => {
            // 保留系統基準和仍存在的自訂基準
            if (!key.startsWith('custom_')) return true;
            return validCustomKeys.has(key);
          });
          // 如果有清理，同步更新 localStorage 和 API
          if (cleaned.length !== prev.length) {
            savePreferences(cleaned);
          }
          return cleaned.length > 0 ? cleaned : ['All Country'];
        });
      } catch (err) {
        console.error('Failed to load custom benchmarks:', err);
      }
    };
    loadCustomBenchmarks();
  }, [savePreferences]);

  /**
   * 當年度或 benchmark 選擇變動時，更新 benchmark 報酬。
   *
   * 規則：
   * - 當年度：用 YTD API（`marketDataApi.getYtdComparison`）資料。
   * - 歷史年度：用 `marketDataApi.getBenchmarkReturns(year)` 的快取快照。
   *
   * UI 策略：
   * - 避免閃爍（FR-095）：更新時不清空舊值，等新值回來再覆蓋。
   */
  useEffect(() => {
    const fetchBenchmarkReturns = async () => {
      if (!selectedYear || !availableYears || selectedBenchmarks.length === 0) {
        // Keep loading state true until we have the necessary data
        return;
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;

      // For current year, wait until ytdData is loaded before proceeding
      // This ensures we show loading state while YTD API is fetching
      if (isCurrentYear && !ytdData) {
        // Keep loading state true - will re-run when ytdData arrives
        setIsLoadingBenchmark(true);
        return;
      }

      setIsLoadingBenchmark(true);
      // Don't clear previous values to prevent flicker (FR-095)

      try {
        const newReturns: Record<string, number | null> = {};

        if (isCurrentYear && ytdData) {
          // Use YTD data for current year - lookup by English key (matches backend)
          for (const benchmarkKey of selectedBenchmarks) {
            const benchmark = ytdData.benchmarks.find(b => b.marketKey === benchmarkKey);
            if (benchmark?.ytdReturnPercent != null) {
              newReturns[benchmarkKey] = benchmark.ytdReturnPercent;
            } else {
              newReturns[benchmarkKey] = null;
            }
          }
        } else {
          // For historical years, use cached benchmark returns from IndexPriceSnapshot
          try {
            const benchmarkData = await marketDataApi.getBenchmarkReturns(selectedYear);
            for (const benchmarkKey of selectedBenchmarks) {
              const returnValue = benchmarkData.returns[benchmarkKey];
              newReturns[benchmarkKey] = returnValue ?? null;
            }
          } catch {
            // If the new API fails, all benchmarks get null
            for (const benchmarkKey of selectedBenchmarks) {
              newReturns[benchmarkKey] = null;
            }
          }
        }

        setBenchmarkReturns(prev => ({ ...prev, ...newReturns }));
      } catch (err) {
        console.error('Failed to fetch benchmark returns:', err);
      } finally {
        setIsLoadingBenchmark(false);
      }
    };

    fetchBenchmarkReturns();
  }, [selectedYear, selectedBenchmarks, availableYears, ytdData]);

  /**
   * 計算自訂 benchmark 的年度報酬。
   *
   * 規則：
   * - 抓取 year-start (上年 12/31) 和 year-end (當年 12/31 或即時) 價格
   * - 計算 (end - start) / start * 100
   */
  useEffect(() => {
    const fetchCustomBenchmarkReturns = async () => {
      if (!selectedYear || !availableYears || customBenchmarks.length === 0) {
        setCustomBenchmarkReturns({});
        return;
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;
      const newReturns: Record<string, number | null> = {};

      await Promise.all(
        customBenchmarks.map(async (benchmark) => {
          try {
            const yearStartDate = `${selectedYear - 1}-12-31`;
            const yearEndDate = `${selectedYear}-12-31`;

            // Get year-start price
            const startPriceData = await marketDataApi.getHistoricalPrices(
              [benchmark.ticker],
              yearStartDate,
              { [benchmark.ticker]: benchmark.market }
            );
            const startPrice = startPriceData[benchmark.ticker]?.price;

            if (!startPrice) {
              newReturns[benchmark.id] = null;
              return;
            }

            let endPrice: number | undefined;

            if (isCurrentYear) {
              // For current year, get live quote
              try {
                // Use Euronext API for EU market (4)
                if (benchmark.market === 4) {
                  const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                  endPrice = euronextQuote?.price;
                } else {
                  const quote = await stockPriceApi.getQuote(benchmark.market, benchmark.ticker);
                  endPrice = quote?.price;
                }
              } catch {
                // Fallback: try Euronext if standard market fails
                if (benchmark.market === 4) {
                  const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                  endPrice = euronextQuote?.price;
                }
              }
            } else {
              // For historical year, get year-end price
              const endPriceData = await marketDataApi.getHistoricalPrices(
                [benchmark.ticker],
                yearEndDate,
                { [benchmark.ticker]: benchmark.market }
              );
              endPrice = endPriceData[benchmark.ticker]?.price;
            }

            if (endPrice && startPrice > 0) {
              newReturns[benchmark.id] = ((endPrice - startPrice) / startPrice) * 100;
            } else {
              newReturns[benchmark.id] = null;
            }
          } catch (err) {
            console.error(`Failed to calculate return for ${benchmark.ticker}:`, err);
            newReturns[benchmark.id] = null;
          }
        })
      );

      setCustomBenchmarkReturns(newReturns);
    };

    fetchCustomBenchmarkReturns();
  }, [selectedYear, availableYears, customBenchmarks]);

  /**
   * 從 localStorage 載入報價快取（與 Portfolio/Dashboard 共用 quote cache）。
   *
   * 使用時機：
   * - 在補齊缺漏價格前，先用快取減少 API 呼叫與等待。
   */
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

  /**
   * 補齊缺漏 ticker 的「即時」報價（通常用於當年度/YTD）。
   *
   * 規則：
   * - 若 MissingPrice 帶有 market = 4 (Euronext)，走 Euronext API。
   * - 其餘用 `stockPriceApi.getQuoteWithRate`，若推測為 US 但失敗則嘗試 UK。
   */
  const fetchCurrentPrices = useCallback(async (
    missingPrices: MissingPrice[],
    homeCurrency: string
  ): Promise<Record<string, YearEndPriceInfo>> => {
    const prices: Record<string, YearEndPriceInfo> = {};

    await Promise.all(missingPrices.map(async (mp) => {
      try {
        // Check if this is a Euronext ticker (market = 4)
        if (mp.market === 4) {
          const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(mp.ticker, homeCurrency);
          if (euronextQuote?.exchangeRate) {
            prices[mp.ticker] = {
              price: euronextQuote.price,
              exchangeRate: euronextQuote.exchangeRate,
            };
          }
          return;
        }

        // Standard market handling
        const market = mp.market ?? guessMarket(mp.ticker);
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

  /**
   * 補齊「歷史年度」的缺漏價格。
   *
   * 資料來源：
   * - `marketDataApi.getHistoricalPrices`：透過 backend 取得歷史收盤價（國際股用 Stooq，台股用 TWSE）。
   * - `marketDataApi.getHistoricalExchangeRate`：取得對應日期的歷史匯率（目前特別針對 homeCurrency=TWD）。
   *
   * 回傳：
   * - yearStartPrices：以上一年度 12/31 作為 year start
   * - yearEndPrices：以當年度 12/31 作為 year end
   */
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

    // Collect unique currencies needed for exchange rate lookup
    const currenciesNeeded = new Set<string>();

    // Fetch year-start prices from prior year Dec 31
    // Backend handles both international (Stooq) and Taiwan (TWSE) stocks
    const yearStartDate = `${year - 1}-12-31`;
    if (yearStartMissing.length > 0) {
      const tickers = yearStartMissing.map(mp => mp.ticker);
      // 建立 ticker 到 market 的對應表
      const markets: Record<string, number | null> = {};
      for (const mp of yearStartMissing) {
        if (mp.market !== undefined) {
          markets[mp.ticker] = mp.market;
        }
      }

      try {
        const stooqPrices = await marketDataApi.getHistoricalPrices(tickers, yearStartDate, markets);

        for (const mp of yearStartMissing) {
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

        for (const mp of yearStartMissing) {
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
    if (yearEndMissing.length > 0) {
      const tickers = yearEndMissing.map(mp => mp.ticker);
      const yearEndCurrenciesNeeded = new Set<string>();
      // 建立 ticker 到 market 的對應表
      const yearEndMarkets: Record<string, number | null> = {};
      for (const mp of yearEndMissing) {
        if (mp.market !== undefined) {
          yearEndMarkets[mp.ticker] = mp.market;
        }
      }

      try {
        const stooqPrices = await marketDataApi.getHistoricalPrices(tickers, yearEndDate, yearEndMarkets);

        for (const mp of yearEndMissing) {
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

        for (const mp of yearEndMissing) {
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

  /**
   * 匯率查詢失敗時的 fallback（僅作最後手段）。
   *
   * 注意：這些是硬編碼估值，用於避免完全無法計算，但不保證準確。
   */
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

  /**
   * 當 `useHistoricalPerformance` 回報有缺漏價格時，自動嘗試補價。
   *
   * 規則：
   * - 當年度：先讀 quote cache，再用即時 API 補剩下的。
   * - 歷史年度：以 Stooq 歷史價 + 歷史匯率補 year-start/year-end。
   *
   * 透過 `hasFetchedForYearRef` 避免同年度重複自動抓取。
   */
  useEffect(() => {
    const autoFetchPrices = async () => {
      if (!performance || !portfolio || !selectedYear || !availableYears) return;
      if (performance.missingPrices.length === 0) {
        fetchRetryCountRef.current = 0; // Reset retry count when no missing prices
        return;
      }
      if (hasFetchedForYearRef.current === selectedYear) return; // Already fetched for this year

      // Limit auto-retries to prevent infinite loops (max 2 retries)
      if (fetchRetryCountRef.current >= 2) {
        setPriceFetchFailed(true);
        return;
      }

      // Mark as fetching in progress (use negative value to indicate in-progress)
      const fetchingMarker = -selectedYear;
      if (hasFetchedForYearRef.current === fetchingMarker) return; // Already fetching
      hasFetchedForYearRef.current = fetchingMarker;
      fetchRetryCountRef.current += 1;

      setPriceFetchFailed(false);

      try {
        const isCurrentYear = selectedYear === availableYears.currentYear;
        let allFetched = false;

        if (isCurrentYear) {
          // YTD: Use cached prices first, then fetch remaining from Sina/Euronext
          const tickers = performance.missingPrices.map(mp => mp.ticker);
          const cachedPrices = loadCachedPrices(tickers);

          // If we have cached prices, immediately calculate with them (no loading spinner)
          // This provides instant feedback while we fetch fresh prices in the background
          const cachedCount = Object.keys(cachedPrices).length;
          if (cachedCount > 0) {
            // Calculate immediately with cached prices (no spinner shown)
            calculatePerformance(selectedYear, cachedPrices);
          }

          const stillMissing = performance.missingPrices.filter(
            mp => !cachedPrices[mp.ticker]
          );

          // Only show loading spinner if we need to fetch prices
          let fetchedPrices: Record<string, YearEndPriceInfo> = {};
          if (stillMissing.length > 0) {
            setIsFetchingPrices(true);
            fetchedPrices = await fetchCurrentPrices(stillMissing, portfolio.homeCurrency);
          }

          const allPrices = { ...cachedPrices, ...fetchedPrices };
          const fetchedCount = Object.keys(allPrices).length;

          // Only call calculatePerformance again if we fetched new prices
          // (if we only had cached prices, we already calculated above)
          if (Object.keys(fetchedPrices).length > 0 && fetchedCount > 0) {
            calculatePerformance(selectedYear, allPrices);
          }

          // Check if all prices fetched
          allFetched = fetchedCount >= performance.missingPrices.length;
          if (!allFetched) {
            setPriceFetchFailed(true);
          }
        } else {
          // Historical year: Use Stooq for international stocks
          setIsFetchingPrices(true);
          const { yearStartPrices, yearEndPrices } = await fetchHistoricalPrices(
            performance.missingPrices,
            selectedYear,
            portfolio.homeCurrency
          );

          const hasPrices = Object.keys(yearEndPrices).length > 0 || Object.keys(yearStartPrices).length > 0;
          if (hasPrices) {
            calculatePerformance(selectedYear, yearEndPrices, yearStartPrices);
          }

          // Check if all prices fetched
          const totalFetched = Object.keys(yearEndPrices).length + Object.keys(yearStartPrices).length;
          allFetched = totalFetched >= performance.missingPrices.length;
          if (!allFetched) {
            setPriceFetchFailed(true);
          }
        }

        // Only mark as fetched if all prices were retrieved
        // This allows retry on next render if partial fetch occurred
        if (allFetched) {
          hasFetchedForYearRef.current = selectedYear;
        } else {
          // Reset to allow retry (clear the fetching marker)
          hasFetchedForYearRef.current = null;
        }
      } catch (err) {
        console.error('Failed to auto-fetch prices:', err);
        setPriceFetchFailed(true);
        // Reset to allow retry on error
        hasFetchedForYearRef.current = null;
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
    fetchRetryCountRef.current = 0; // Reset retry count for manual refresh

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
    setPriceFetchFailed(false);
    hasFetchedForYearRef.current = null; // Reset to allow fresh fetch for new year
    fetchRetryCountRef.current = 0; // Reset retry count for new year
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
            {/* Missing Prices Overlay - Full screen modal when fetching or missing prices */}
            {performance.missingPrices.length > 0 && (
              <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                <div className="card-dark w-full max-w-md mx-4">
                  <div className="px-5 py-4 border-b border-[var(--border-color)]">
                    <h3 className="text-lg font-bold text-[var(--text-primary)]">
                      {isFetchingPrices ? '正在抓取價格...' : '缺少股票價格'}
                    </h3>
                  </div>
                  <div className="p-5">
                    {(() => {
                      // Dedupe tickers for display
                      const uniqueTickers = [...new Set(performance.missingPrices.map(mp => mp.ticker))];
                      return (
                        <>
                          <p className="text-[var(--text-secondary)] mb-4">
                            缺少 {uniqueTickers.length} 支股票的
                            {selectedYear === availableYears?.currentYear ? '即時報價' : `${selectedYear} 年度價格`}
                          </p>
                          <div className="bg-[var(--bg-tertiary)] rounded-lg p-3 max-h-[200px] overflow-y-auto mb-4">
                            <ul className="space-y-1 text-sm">
                              {uniqueTickers.slice(0, 10).map((ticker) => (
                                <li key={ticker} className="text-[var(--text-muted)]">
                                  • {ticker}
                                </li>
                              ))}
                              {uniqueTickers.length > 10 && (
                                <li className="text-[var(--text-muted)]">
                                  ... 還有 {uniqueTickers.length - 10} 支
                                </li>
                              )}
                            </ul>
                          </div>
                        </>
                      );
                    })()}
                    {isFetchingPrices && (
                      <div className="flex items-center justify-center gap-2 py-4">
                        <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                        <span className="text-[var(--text-muted)]">
                          {selectedYear === availableYears?.currentYear
                            ? '正在抓取即時報價...'
                            : `正在抓取 ${selectedYear} 年度價格...`}
                        </span>
                      </div>
                    )}
                    {!isFetchingPrices && priceFetchFailed && (
                      <div className="bg-[var(--color-warning)]/10 border border-[var(--color-warning)]/30 rounded-lg p-3 mb-4">
                        <p className="text-sm text-[var(--color-warning)]">
                          無法自動取得歷史價格。外部資料來源可能暫時無法使用，請稍後再試或手動輸入價格。
                        </p>
                      </div>
                    )}
                  </div>
                  <div className="px-5 py-4 border-t border-[var(--border-color)] flex justify-end gap-3">
                    {!isFetchingPrices && (
                      <>
                        <button
                          type="button"
                          onClick={handleRefreshPrices}
                          className="btn-dark px-4 py-2 flex items-center gap-2"
                        >
                          <RefreshCw className="w-4 h-4" />
                          重新抓取
                        </button>
                        <button
                          type="button"
                          onClick={() => setShowMissingPriceModal(true)}
                          className="btn-accent px-4 py-2"
                        >
                          手動輸入
                        </button>
                      </>
                    )}
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
                    <div className="relative group">
                      <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                      <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                        <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                          原幣報酬率（不含匯率變動）
                        </div>
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
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
                    <XirrWarningBadge
                      earliestTransactionDate={performance.earliestTransactionDateInYear}
                      asOfDate={selectedYear === new Date().getFullYear()
                        ? new Date().toISOString()
                        : `${selectedYear}-12-31`}
                    />
                  </div>
                </div>
              )}

              {/* Home Currency XIRR Card (TWD) */}
              <div className="card-dark p-6">
                <div className="flex items-center gap-2 mb-2">
                  <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                  <h3 className="text-[var(--text-muted)]">
                    {selectedYear} 年度 XIRR ({portfolio.homeCurrency})
                  </h3>
                  <div className="relative group">
                    <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                    <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                      <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                        {performance.transactionCount} 筆交易（含匯率變動）
                      </div>
                    </div>
                  </div>
                </div>
                {performance.xirrPercentage != null ? (
                  <div className="flex items-center gap-2">
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
                    <XirrWarningBadge
                      earliestTransactionDate={performance.earliestTransactionDateInYear}
                      asOfDate={selectedYear === new Date().getFullYear()
                        ? new Date().toISOString()
                        : `${selectedYear}-12-31`}
                    />
                  </div>
                ) : isFetchingPrices ? (
                  <div className="flex items-center gap-2">
                    <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                    <span className="text-lg text-[var(--text-muted)]">抓取價格中...</span>
                  </div>
                ) : (
                  <span className="text-2xl text-[var(--text-muted)]">需要價格資料</span>
                )}
              </div>
            </div>

            {/* Value Summary - Source Currency */}
            {/* FR-133: Always show source currency block, including first year */}
            {performance.sourceCurrency && (
              <div className="card-dark p-6 mb-6 border-l-4 border-[var(--accent-peach)]">
                <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                  {selectedYear} 年度摘要 ({performance.sourceCurrency})
                </h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">年初價值</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {/* FR-133: Show "首年" when no prior holdings exist */}
                      {performance.startValueSource == null || performance.startValueSource === 0
                        ? '首年'
                        : `${formatCurrency(performance.startValueSource)} ${performance.sourceCurrency}`}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">
                      {selectedYear === availableYears?.currentYear ? '目前價值' : '年底價值'}
                    </p>
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
                    {/* FR-133: Show "首年" when no prior holdings exist */}
                    {performance.startValueHome == null || performance.startValueHome === 0
                      ? '首年'
                      : `${formatCurrency(performance.startValueHome)} ${portfolio.homeCurrency}`}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">
                    {selectedYear === availableYears?.currentYear ? '目前價值' : '年底價值'}
                  </p>
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

            {/* Performance Bar Chart - Portfolio vs Benchmarks (Multi-select) */}
            {/* FR-132/T130: For current year, show loading state until prices are ready */}
            {selectedYear === availableYears?.currentYear && (isFetchingPrices || performance.missingPrices.length > 0) ? (
              <div className="card-dark p-6 mt-6">
                <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">績效比較</h3>
                <div className="flex items-center justify-center py-8">
                  <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                  <span className="ml-2 text-[var(--text-muted)]">正在取得即時股價以計算績效...</span>
                </div>
              </div>
            ) : performance.xirrPercentageSource != null && (
              <div className="card-dark p-6 mt-6">
                <div className="flex justify-between items-center mb-4">
                  <h3 className="text-lg font-bold text-[var(--text-primary)]">
                    績效比較
                  </h3>
                  <div className="relative">
                    <button
                      type="button"
                      onClick={() => {
                        setTempSelectedBenchmarks(selectedBenchmarks);
                        setShowBenchmarkSettings(true);
                      }}
                      className="btn-dark p-2 h-8 flex items-center justify-center"
                      title="選擇比較基準"
                    >
                      <Settings className="w-4 h-4" />
                    </button>
                  </div>
                </div>

                {/* Benchmark Settings Modal - Dashboard style */}
                {showBenchmarkSettings && (
                  <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="card-dark w-full max-w-md mx-4">
                      <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
                        <h3 className="text-lg font-bold text-[var(--text-primary)]">選擇比較基準</h3>
                        <button
                          type="button"
                          onClick={() => setShowBenchmarkSettings(false)}
                          className="text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                        >
                          <X className="w-5 h-5" />
                        </button>
                      </div>
                      <div className="p-5 max-h-[50vh] overflow-y-auto">
                        {tempSelectedBenchmarks.length >= 10 && (
                          <div className="mb-3 px-3 py-2 bg-[var(--accent-peach)]/10 border border-[var(--accent-peach)]/30 rounded-lg text-sm text-[var(--text-muted)]">
                            已達上限（最多 10 個）
                          </div>
                        )}
                        {/* 系統內建基準 */}
                        <div className="mb-4">
                          <h4 className="text-xs text-[var(--text-muted)] mb-2">系統內建基準</h4>
                          <div className="grid grid-cols-2 gap-2">
                          {BENCHMARK_OPTIONS.map((option) => {
                            const isSelected = tempSelectedBenchmarks.includes(option.key);
                            const isAtLimit = tempSelectedBenchmarks.length >= 10;
                            const isDisabled = !isSelected && isAtLimit;

                            return (
                            <button
                              key={option.key}
                              type="button"
                              onClick={() => {
                                if (isSelected) {
                                  if (tempSelectedBenchmarks.length > 1) {
                                    setTempSelectedBenchmarks(tempSelectedBenchmarks.filter(k => k !== option.key));
                                  }
                                } else if (!isAtLimit) {
                                  setTempSelectedBenchmarks([...tempSelectedBenchmarks, option.key]);
                                }
                              }}
                              disabled={isDisabled}
                              className={`flex items-center gap-2 px-3 py-2 rounded-lg border transition-colors text-left ${
                                isSelected
                                  ? 'border-[var(--accent-peach)] bg-[var(--accent-peach)]/10 text-[var(--text-primary)]'
                                  : isDisabled
                                    ? 'border-[var(--border-color)] text-[var(--text-muted)] opacity-50 cursor-not-allowed'
                                    : 'border-[var(--border-color)] text-[var(--text-muted)] hover:border-[var(--text-muted)]'
                              }`}
                            >
                              <div className={`w-4 h-4 rounded border flex items-center justify-center shrink-0 ${
                                isSelected
                                  ? 'bg-[var(--accent-peach)] border-[var(--accent-peach)]'
                                  : 'border-[var(--text-muted)]'
                              }`}>
                                {isSelected && <Check className="w-3 h-3 text-[var(--bg-primary)]" />}
                              </div>
                              <span className="text-sm truncate">{option.label}</span>
                            </button>
                          );
                          })}
                          </div>
                        </div>
                        {/* 自訂基準 */}
                        {customBenchmarks.length > 0 && (
                          <div>
                            <h4 className="text-xs text-[var(--text-muted)] mb-2">自訂基準</h4>
                            <div className="grid grid-cols-2 gap-2">
                              {customBenchmarks.map((b) => {
                                const customKey = `custom_${b.id}`;
                                const isSelected = tempSelectedBenchmarks.includes(customKey);
                                const isAtLimit = tempSelectedBenchmarks.length >= 10;
                                const isDisabled = !isSelected && isAtLimit;

                                return (
                                  <button
                                    key={customKey}
                                    type="button"
                                    onClick={() => {
                                      if (isSelected) {
                                        if (tempSelectedBenchmarks.length > 1) {
                                          setTempSelectedBenchmarks(tempSelectedBenchmarks.filter(k => k !== customKey));
                                        }
                                      } else if (!isAtLimit) {
                                        setTempSelectedBenchmarks([...tempSelectedBenchmarks, customKey]);
                                      }
                                    }}
                                    disabled={isDisabled}
                                    className={`flex items-center gap-2 px-3 py-2 rounded-lg border transition-colors text-left ${
                                      isSelected
                                        ? 'border-[var(--accent-peach)] bg-[var(--accent-peach)]/10 text-[var(--text-primary)]'
                                        : isDisabled
                                          ? 'border-[var(--border-color)] text-[var(--text-muted)] opacity-50 cursor-not-allowed'
                                          : 'border-[var(--border-color)] text-[var(--text-muted)] hover:border-[var(--text-muted)]'
                                    }`}
                                  >
                                    <div className={`w-4 h-4 rounded border flex items-center justify-center shrink-0 ${
                                      isSelected
                                        ? 'bg-[var(--accent-peach)] border-[var(--accent-peach)]'
                                        : 'border-[var(--text-muted)]'
                                    }`}>
                                      {isSelected && <Check className="w-3 h-3 text-[var(--bg-primary)]" />}
                                    </div>
                                    <span className="text-sm truncate">{b.displayName || b.ticker}</span>
                                  </button>
                                );
                              })}
                            </div>
                          </div>
                        )}
                      </div>
                      <div className="px-5 py-4 border-t border-[var(--border-color)] flex justify-end gap-3">
                        <button
                          type="button"
                          onClick={() => setShowBenchmarkSettings(false)}
                          className="btn-dark px-4 py-2"
                        >
                          取消
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            if (tempSelectedBenchmarks.length > 0) {
                              setSelectedBenchmarks(tempSelectedBenchmarks);
                              savePreferences(tempSelectedBenchmarks);
                            }
                            setShowBenchmarkSettings(false);
                          }}
                          className="btn-accent px-4 py-2"
                        >
                          儲存
                        </button>
                      </div>
                    </div>
                  </div>
                )}
                {/* Show loading if: loading state, OR version mismatch (stale data), OR no benchmark data yet */}
                {(isLoadingBenchmark || lastResetVersionRef.current !== performanceVersion || Object.keys(benchmarkReturns).length === 0) ? (
                  <div className="flex items-center justify-center py-8">
                    <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                    <span className="ml-2 text-[var(--text-muted)]">載入基準報酬中...</span>
                  </div>
                ) : (
                  <>
                    <PerformanceBarChart
                      data={[
                        {
                          label: `我的 XIRR (${performance.sourceCurrency})`,
                          value: performance.xirrPercentageSource,
                          tooltip: `${selectedYear} 年化報酬率 (${performance.transactionCount} 筆交易)`,
                        },
                        /* FR-134: Filter out benchmarks with null data instead of showing 0 */
                        ...selectedBenchmarks
                          .filter(benchmarkKey => benchmarkReturns[benchmarkKey] != null)
                          .map(benchmarkKey => {
                            const benchmarkInfo = BENCHMARK_OPTIONS.find(b => b.key === benchmarkKey);
                            const returnValue = benchmarkReturns[benchmarkKey]!;
                            return {
                              label: benchmarkInfo?.label ?? benchmarkKey,
                              value: returnValue,
                              tooltip: `${selectedYear} 年度報酬率`,
                            };
                          }),
                        /* Custom user benchmarks - only show selected ones */
                        ...customBenchmarks
                          .filter(b => {
                            const customKey = `custom_${b.id}`;
                            return selectedBenchmarks.includes(customKey) && customBenchmarkReturns[b.id] != null;
                          })
                          .map(b => ({
                            label: b.displayName || b.ticker,
                            value: customBenchmarkReturns[b.id]!,
                            tooltip: `${selectedYear} 年度報酬率（自訂）`,
                          })),
                      ]}
                      height={80 + (selectedBenchmarks.filter(k => benchmarkReturns[k] != null).length + customBenchmarks.filter(b => selectedBenchmarks.includes(`custom_${b.id}`) && customBenchmarkReturns[b.id] != null).length) * 40}
                    />
                    {/* FR-134: Show which benchmarks are hidden due to missing data */}
                    {(selectedBenchmarks.some(k => !k.startsWith('custom_') && benchmarkReturns[k] == null) || customBenchmarks.some(b => selectedBenchmarks.includes(`custom_${b.id}`) && customBenchmarkReturns[b.id] == null)) && (
                      <p className="text-xs text-[var(--color-warning)] mt-2">
                        以下指數因資料不可用已隱藏：
                        {[
                          ...selectedBenchmarks
                            .filter(k => !k.startsWith('custom_') && benchmarkReturns[k] == null)
                            .map(k => BENCHMARK_OPTIONS.find(b => b.key === k)?.label ?? k),
                          ...customBenchmarks
                            .filter(b => selectedBenchmarks.includes(`custom_${b.id}`) && customBenchmarkReturns[b.id] == null)
                            .map(b => b.displayName || b.ticker),
                        ].join('、')}
                      </p>
                    )}
                  </>
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
