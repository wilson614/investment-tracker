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
import { DEFAULT_BENCHMARKS } from '../constants';
import { stockPriceApi, marketDataApi, userBenchmarkApi, userPreferencesApi } from '../services/api';
import { loadCachedYtdData, getYtdData, transformYtdData } from '../services/ytdApi';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { usePortfolio } from '../contexts/PortfolioContext';
import { YearSelector } from '../components/performance/YearSelector';
import { CurrencyToggle, type PerformanceCurrencyMode } from '../components/performance/CurrencyToggle';
import { MissingPriceModal } from '../components/modals/MissingPriceModal';
import { PerformanceBarChart } from '../components/charts';
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

// 與 Dashboard MarketYtdSection 共用的 localStorage key
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
        // 驗證 key：系統基準必須存在於 BENCHMARK_OPTIONS，自訂基準以 'custom_' 開頭
        const validKeys = keys.filter(k =>
          BENCHMARK_OPTIONS.some(o => o.key === k) || k.startsWith('custom_')
        );
        if (validKeys.length > 0) return validKeys;
      }
    }
  } catch {
    // Ignore
  }
  return [...DEFAULT_BENCHMARKS];
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

// 報價快取 key（與 Portfolio 頁面共用）
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

/**
 * 儲存報價至 localStorage 快取（與 Portfolio 頁面共用）
 */
function saveQuoteToCache(ticker: string, price: number, exchangeRate: number): void {
  try {
    const cacheData = {
      quote: { price, exchangeRate },
      timestamp: Date.now(),
    };
    localStorage.setItem(getQuoteCacheKey(ticker), JSON.stringify(cacheData));
  } catch {
    // Ignore
  }
}

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

// 自訂基準報酬快取 key
const getCustomBenchmarkCacheKey = (year: number) => `custom_benchmark_returns_${year}`;

// 自訂基準列表快取 key
const CUSTOM_BENCHMARKS_CACHE_KEY = 'custom_benchmarks_list';

/**
 * 載入自訂基準列表快取
 */
function loadCachedCustomBenchmarks(): UserBenchmark[] {
  try {
    const cached = localStorage.getItem(CUSTOM_BENCHMARKS_CACHE_KEY);
    if (cached) {
      return JSON.parse(cached);
    }
  } catch { /* 忽略 */ }
  return [];
}

/**
 * 儲存自訂基準列表快取
 */
function saveCachedCustomBenchmarks(benchmarks: UserBenchmark[]): void {
  try {
    localStorage.setItem(CUSTOM_BENCHMARKS_CACHE_KEY, JSON.stringify(benchmarks));
  } catch { /* 忽略 */ }
}

/**
 * 載入自訂基準報酬快取
 */
function loadCachedCustomBenchmarkReturns(year: number): Record<string, number | null> {
  try {
    const cached = localStorage.getItem(getCustomBenchmarkCacheKey(year));
    if (cached) {
      return JSON.parse(cached);
    }
  } catch { /* 忽略 */ }
  return {};
}

/**
 * 儲存自訂基準報酬快取
 */
function saveCachedCustomBenchmarkReturns(year: number, data: Record<string, number | null>): void {
  try {
    localStorage.setItem(getCustomBenchmarkCacheKey(year), JSON.stringify(data));
  } catch { /* 忽略 */ }
}

// 系統基準報酬快取 key
const getSystemBenchmarkCacheKey = (year: number) => `system_benchmark_returns_${year}`;

/**
 * 載入系統基準報酬快取
 */
function loadCachedSystemBenchmarkReturns(year: number): { returns: Record<string, number | null>; sources: Record<string, string | null> } {
  try {
    const cached = localStorage.getItem(getSystemBenchmarkCacheKey(year));
    if (cached) {
      return JSON.parse(cached);
    }
  } catch { /* 忽略 */ }
  return { returns: {}, sources: {} };
}

/**
 * 儲存系統基準報酬快取
 */
function saveCachedSystemBenchmarkReturns(year: number, returns: Record<string, number | null>, sources: Record<string, string | null>): void {
  try {
    localStorage.setItem(getSystemBenchmarkCacheKey(year), JSON.stringify({ returns, sources }));
  } catch { /* 忽略 */ }
}

export function PerformancePage() {
  // 使用共用的投資組合 context（與 Portfolio 頁面同步）
  const { currentPortfolio: portfolio, isLoading: isLoadingPortfolio, performanceVersion } = usePortfolio();

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

  // 使用 key 強制在投資組合變更時重新掛載元件
  // 確保 useHistoricalPerformance 的 lazy init 正確運作
  return (
    <PerformancePageContent
      key={`${portfolio.id}-${performanceVersion}`}
      portfolio={portfolio}
    />
  );
}

// 內部元件：僅在 portfolio 可用時掛載
// 確保 useHistoricalPerformance 的 lazy init 能正確從快取載入
function PerformancePageContent({ portfolio }: { portfolio: NonNullable<ReturnType<typeof usePortfolio>['currentPortfolio']> }) {
  const [showMissingPriceModal, setShowMissingPriceModal] = useState(false);
  const [isFetchingPrices, setIsFetchingPrices] = useState(false);
  const [priceFetchFailed, setPriceFetchFailed] = useState(false);
  const [dismissMissingPricesOverlay, setDismissMissingPricesOverlay] = useState(false);
  const hasFetchedForYearRef = useRef<number | null>(null);
  const fetchRetryCountRef = useRef<number>(0); // 限制自動重試次數以防止無限迴圈

  // 基準比較狀態 - 支援多選，與 Dashboard 同步
  const [selectedBenchmarks, setSelectedBenchmarks] = useState<string[]>(loadSelectedBenchmarksFromLocalStorage);
  const [tempSelectedBenchmarks, setTempSelectedBenchmarks] = useState<string[]>([]);
  const [showBenchmarkSettings, setShowBenchmarkSettings] = useState(false);

  // 從快取 lazy init ytdData 以即時顯示
  const [ytdData, setYtdData] = useState<MarketYtdComparison | null>(() => {
    const cached = loadCachedYtdData();
    return cached.data ? transformYtdData(cached.data) : null;
  });

  // 從 YTD 快取（當年度）或系統基準快取（歷史年度）lazy init benchmarkReturns
  // 避免只有投資組合長條先顯示，基準稍後才出現的閃爍問題
  const [benchmarkReturns, setBenchmarkReturns] = useState<Record<string, number | null>>(() => {
    const currentYear = new Date().getFullYear();
    const savedBenchmarks = loadSelectedBenchmarksFromLocalStorage();
    const systemBenchmarks = savedBenchmarks.filter(k => !k.startsWith('custom_'));

    // 優先從 YTD 快取載入（當年度）
    const cachedYtd = loadCachedYtdData();
    if (cachedYtd.data) {
      const ytd = transformYtdData(cachedYtd.data);
      const returns: Record<string, number | null> = {};
      // 載入所有選擇的系統基準，包括值為 null 的情況
      for (const key of systemBenchmarks) {
        const benchmark = ytd.benchmarks.find(b => b.marketKey === key);
        // 使用 null 表示「有快取但無資料」，undefined 表示「沒有快取」
        returns[key] = benchmark?.ytdReturnPercent ?? null;
      }
      if (Object.keys(returns).length > 0) {
        return returns;
      }
    }

    // 備援：嘗試系統基準快取（歷史年度）
    const systemCache = loadCachedSystemBenchmarkReturns(currentYear);
    if (Object.keys(systemCache.returns).length > 0) {
      // 確保所有選擇的基準都有值（即使是 null）
      const returns: Record<string, number | null> = {};
      for (const key of systemBenchmarks) {
        returns[key] = systemCache.returns[key] ?? null;
      }
      return returns;
    }

    return {};
  });
  const [benchmarkReturnSources, setBenchmarkReturnSources] = useState<Record<string, string | null>>(() => {
    const currentYear = new Date().getFullYear();
    const systemCache = loadCachedSystemBenchmarkReturns(currentYear);
    return systemCache.sources;
  });
  // 追蹤基準載入狀態，用於決定何時渲染長條圖
  // 初始值設為 false，因為我們已經在 lazy init 中載入了快取資料
  // 只有在確定沒有任何快取時才會設為 true
  const [, setIsLoadingBenchmark] = useState(() => {
    // 檢查 YTD 快取（當年度主要來源）
    const cachedYtd = loadCachedYtdData();
    if (cachedYtd.data) {
      return false; // 有 YTD 快取，不需要 loading
    }
    // 備援：檢查系統基準快取
    const currentYear = new Date().getFullYear();
    const systemCache = loadCachedSystemBenchmarkReturns(currentYear);
    return Object.keys(systemCache.returns).length === 0;
  });
  const [, setIsLoadingCustomBenchmark] = useState(() => {
    const currentYear = new Date().getFullYear();
    const cachedReturns = loadCachedCustomBenchmarkReturns(currentYear);
    const savedBenchmarks = loadSelectedBenchmarksFromLocalStorage();
    // 若沒有選擇自訂基準，則不需要 loading
    const hasCustomSelection = savedBenchmarks.some(k => k.startsWith('custom_'));
    if (!hasCustomSelection) return false;
    // 若有快取資料則初始為 false，否則等待載入
    return Object.keys(cachedReturns).length === 0;
  });

  // US2: 績效比較的貨幣模式 - 從 localStorage 載入
  const [currencyMode, setCurrencyMode] = useState<PerformanceCurrencyMode>(() => {
    try {
      const stored = localStorage.getItem('performance_currency_mode');
      return (stored === 'source' || stored === 'home') ? stored : 'source';
    } catch {
      return 'source';
    }
  });

  // 自訂基準狀態 - 從快取載入以即時顯示
  const [customBenchmarks, setCustomBenchmarks] = useState<UserBenchmark[]>(loadCachedCustomBenchmarks);
  // 自訂基準報酬 - 從快取載入，預設使用當年度
  // 確保所有選擇的自訂基準都有值（即使是 null），以便正確判斷載入狀態
  const [customBenchmarkReturns, setCustomBenchmarkReturns] = useState<Record<string, number | null>>(() => {
    const currentYear = new Date().getFullYear();
    const cachedReturns = loadCachedCustomBenchmarkReturns(currentYear);
    const savedBenchmarks = loadSelectedBenchmarksFromLocalStorage();
    const cachedBenchmarkList = loadCachedCustomBenchmarks();

    // 為所有選擇的自訂基準設定值
    const returns: Record<string, number | null> = {};
    for (const key of savedBenchmarks) {
      if (key.startsWith('custom_')) {
        const benchmarkId = key.replace('custom_', '');
        // 確認這個自訂基準確實存在於快取列表中
        if (cachedBenchmarkList.some(b => b.id === benchmarkId)) {
          returns[benchmarkId] = cachedReturns[benchmarkId] ?? null;
        }
      }
    }
    return Object.keys(returns).length > 0 ? returns : cachedReturns;
  });

  // 元件掛載時從 API 載入使用者偏好設定
  useEffect(() => {
    const loadPreferences = async () => {
      try {
        const prefs = await userPreferencesApi.get();
        if (prefs.ytdBenchmarkPreferences) {
          const benchmarks = JSON.parse(prefs.ytdBenchmarkPreferences) as string[];
          if (Array.isArray(benchmarks) && benchmarks.length > 0) {
            // 驗證 key：系統基準必須存在於 BENCHMARK_OPTIONS，自訂基準以 'custom_' 開頭
            const validKeys = benchmarks.filter(k =>
              BENCHMARK_OPTIONS.some(o => o.key === k) || k.startsWith('custom_')
            );
            if (validKeys.length > 0) {
              setSelectedBenchmarks(validKeys);
              // 同步至 localStorage 供離線使用
              saveSelectedBenchmarksToLocalStorage(validKeys);
            }
          }
        }
      } catch (err) {
        console.error('無法從 API 載入偏好設定，使用 localStorage:', err);
        // 保留 localStorage 的值作為備援
      }
    };
    loadPreferences();
  }, []);

  // 儲存偏好設定至 API
  const savePreferences = useCallback(async (keys: string[]) => {
    // 先儲存至 localStorage（立即同步）
    saveSelectedBenchmarksToLocalStorage(keys);

    // 再儲存至 API
    try {
      await userPreferencesApi.update({
        ytdBenchmarkPreferences: JSON.stringify(keys),
      });
    } catch (err) {
      console.error('無法儲存偏好設定至 API:', err);
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
    portfolioId: portfolio.id, // Always defined in this component
    autoFetch: true,
  });

  // 元件掛載時重設報價抓取狀態（portfolio 透過 key 變更時會重新掛載）
  // 無需依賴項 - 當 portfolio 變更時元件會因 key prop 重新掛載
  // 注意：不清空 benchmarkReturns，因為 lazy init 已載入當年度快取
  useEffect(() => {
    hasFetchedForYearRef.current = null;
    fetchRetryCountRef.current = 0;
    setPriceFetchFailed(false);
    setIsFetchingPrices(false);
  }, []);

  // 載入當年度 YTD 基準資料
  // 快取已透過 lazy init 載入，這裡只在背景抓取最新資料
  useEffect(() => {
    const loadFreshYtdData = async () => {
      try {
        const data = await getYtdData();
        setYtdData(transformYtdData(data));
      } catch (err) {
        console.error('無法載入 YTD 資料:', err);
      }
    };
    loadFreshYtdData();
  }, []);

  // 載入使用者的自訂基準
  useEffect(() => {
    const loadCustomBenchmarks = async () => {
      try {
        const benchmarks = await userBenchmarkApi.getAll();
        setCustomBenchmarks(benchmarks);
        // 快取以便下次載入時即時顯示
        saveCachedCustomBenchmarks(benchmarks);

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
          return cleaned.length > 0 ? cleaned : [...DEFAULT_BENCHMARKS];
        });
      } catch (err) {
        console.error('無法載入自訂基準:', err);
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
        // 在取得必要資料前保持 loading 狀態
        return;
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;

      // 當年度需等待 ytdData 載入完成後再繼續
      // 確保 YTD API 抓取時顯示 loading 狀態
      if (isCurrentYear && !ytdData) {
        // 保持 loading 狀態 - ytdData 到達時會重新執行
        setIsLoadingBenchmark(true);
        return;
      }

      // 歷史年度優先嘗試從快取載入以即時顯示
      // 當年度則檢查 YTD 快取是否已包含選擇的基準
      let hasCachedData = false;
      if (!isCurrentYear) {
        const cached = loadCachedSystemBenchmarkReturns(selectedYear);
        if (Object.keys(cached.returns).length > 0) {
          setBenchmarkReturns(prev => ({ ...prev, ...cached.returns }));
          setBenchmarkReturnSources(prev => ({ ...prev, ...cached.sources }));
          // 檢查所有選擇的系統基準是否都在快取中
          const systemBenchmarks = selectedBenchmarks.filter(k => !k.startsWith('custom_'));
          hasCachedData = systemBenchmarks.every(k => cached.returns[k] !== undefined);
        }
      } else {
        // 當年度：檢查 YTD 快取是否有資料
        const cachedYtd = loadCachedYtdData();
        if (cachedYtd.data) {
          const ytd = transformYtdData(cachedYtd.data);
          const systemBenchmarks = selectedBenchmarks.filter(k => !k.startsWith('custom_'));
          hasCachedData = systemBenchmarks.some(k => ytd.benchmarks.some(b => b.marketKey === k && b.ytdReturnPercent != null));
        }
      }

      // 只在沒有快取資料時才顯示 loading，否則在背景更新
      if (!hasCachedData) {
        setIsLoadingBenchmark(true);
      } else {
        // 有快取資料時確保 loading 為 false（避免初始值問題）
        setIsLoadingBenchmark(false);
      }
      // 不清空舊值以防止閃爍 (FR-095)

      try {
        const newReturns: Record<string, number | null> = {};
        const newSources: Record<string, string | null> = {};

        if (isCurrentYear && ytdData) {
          // 當年度使用 YTD 資料 - 以英文 key 查詢（與後端一致）
          for (const benchmarkKey of selectedBenchmarks) {
            const benchmark = ytdData.benchmarks.find(b => b.marketKey === benchmarkKey);
            if (benchmark?.ytdReturnPercent != null) {
              newReturns[benchmarkKey] = benchmark.ytdReturnPercent;
              newSources[benchmarkKey] = 'Calculated';
            } else {
              newReturns[benchmarkKey] = null;
              newSources[benchmarkKey] = null;
            }
          }
        } else {
          // 歷史年度使用快取的基準報酬（優先 Yahoo Total Return，備援為計算值）
          try {
            const benchmarkData = await marketDataApi.getBenchmarkReturns(selectedYear);
            for (const benchmarkKey of selectedBenchmarks) {
              const returnValue = benchmarkData.returns[benchmarkKey];
              newReturns[benchmarkKey] = returnValue ?? null;
              newSources[benchmarkKey] = benchmarkData.dataSources?.[benchmarkKey] ?? null;
            }
            // 儲存至快取供未來使用
            saveCachedSystemBenchmarkReturns(selectedYear, newReturns, newSources);
          } catch {
            // API 失敗時所有基準為 null
            for (const benchmarkKey of selectedBenchmarks) {
              newReturns[benchmarkKey] = null;
              newSources[benchmarkKey] = null;
            }
          }
        }

        setBenchmarkReturns(prev => ({ ...prev, ...newReturns }));
        setBenchmarkReturnSources(prev => ({ ...prev, ...newSources }));
      } catch (err) {
        console.error('無法抓取基準報酬:', err);
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
   * - 優先使用 localStorage 快取（秒開體驗）。
   * - 抓取 year-start （上年 12/31）和 year-end （當年 12/31 或即時）價格
   * - 計算 (end - start) / start * 100
   * - 更新後寫入快取。
   */
  useEffect(() => {
    const fetchCustomBenchmarkReturns = async () => {
      if (!selectedYear || !availableYears || customBenchmarks.length === 0) {
        setCustomBenchmarkReturns({});
        setIsLoadingCustomBenchmark(false);
        return;
      }

      // 1. 優先嘗試從快取載入以即時回饋
      const cachedReturns = loadCachedCustomBenchmarkReturns(selectedYear);
      const selectedCustomBenchmarks = customBenchmarks.filter(b => selectedBenchmarks.includes(`custom_${b.id}`));

      // 若沒有選擇任何自訂基準，直接完成
      if (selectedCustomBenchmarks.length === 0) {
        setIsLoadingCustomBenchmark(false);
        return;
      }

      // 檢查是否有足夠的快取資料
      let hasCachedData = false;
      if (Object.keys(cachedReturns).length > 0) {
        setCustomBenchmarkReturns(prev => ({ ...prev, ...cachedReturns }));

        // 歷史年度（非當年度）可完全信任快取（若完整）
        const isCurrentYear = selectedYear === availableYears.currentYear;
        if (!isCurrentYear) {
           // 檢查所有選擇的基準是否都在快取中
           const allCached = selectedCustomBenchmarks.every(b => cachedReturns[b.id] !== undefined);

           if (allCached) {
             setIsLoadingCustomBenchmark(false);
             return; // 歷史年度若快取存在則跳過網路請求
           }
        }

        // 當年度：檢查是否有任何快取資料可先顯示
        hasCachedData = selectedCustomBenchmarks.some(b => cachedReturns[b.id] !== undefined);
      }

      // 只在沒有快取資料時才顯示 loading，否則在背景更新
      if (!hasCachedData && selectedCustomBenchmarks.length > 0) {
        setIsLoadingCustomBenchmark(true);
      } else {
        // 有快取資料或沒有選擇自訂基準時，確保 loading 為 false
        setIsLoadingCustomBenchmark(false);
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;

      try {
        // 追蹤收集到的報酬以便稍後儲存至快取
        const collectedReturns: Record<string, number | null> = { ...cachedReturns };

        await Promise.all(
          customBenchmarks.map(async (benchmark) => {
            // 歷史年度若不在快取中則抓取，當年度則總是刷新即時資料
            // 我們已顯示快取資料，所以這次更新只是「刷新」

            let returnValue: number | null = null;
            try {
              const yearStartDate = `${selectedYear - 1}-12-31`;
              const yearEndDate = `${selectedYear}-12-31`;

              // 取得年初價格
              const startPriceData = await marketDataApi.getHistoricalPrices(
                [benchmark.ticker],
                yearStartDate,
                { [benchmark.ticker]: benchmark.market }
              );
              const startPrice = startPriceData[benchmark.ticker]?.price;

              if (!startPrice) {
                returnValue = null;
              } else {
                let endPrice: number | undefined;

                if (isCurrentYear) {
                  // 當年度優先嘗試從快取載入以即時顯示
                  try {
                    const cached = localStorage.getItem(getQuoteCacheKey(benchmark.ticker));
                    if (cached) {
                      const data = JSON.parse(cached);
                      if (data.quote?.price) {
                        endPrice = data.quote.price;
                      }
                    }
                  } catch {
                    // 忽略快取錯誤
                  }

                  // 若無快取則抓取即時報價
                  if (!endPrice) {
                    try {
                      // EU 市場 (4) 使用 Euronext API
                      if (benchmark.market === 4) {
                        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                        endPrice = euronextQuote?.price;
                        if (euronextQuote?.price && euronextQuote?.exchangeRate) {
                          saveQuoteToCache(benchmark.ticker, euronextQuote.price, euronextQuote.exchangeRate);
                        }
                      } else {
                        const quote = await stockPriceApi.getQuote(benchmark.market, benchmark.ticker);
                        endPrice = quote?.price;
                        if (quote?.price) {
                          // Use 1 as default exchange rate for non-Euronext
                          saveQuoteToCache(benchmark.ticker, quote.price, 1);
                        }
                      }
                    } catch {
                      // 備援：若標準市場失敗則嘗試 Euronext
                      if (benchmark.market === 4) {
                        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                        endPrice = euronextQuote?.price;
                        if (euronextQuote?.price && euronextQuote?.exchangeRate) {
                          saveQuoteToCache(benchmark.ticker, euronextQuote.price, euronextQuote.exchangeRate);
                        }
                      }
                    }
                  }
                } else {
                  // 歷史年度取得年底價格
                  const endPriceData = await marketDataApi.getHistoricalPrices(
                    [benchmark.ticker],
                    yearEndDate,
                    { [benchmark.ticker]: benchmark.market }
                  );
                  endPrice = endPriceData[benchmark.ticker]?.price;
                }

                if (endPrice && startPrice > 0) {
                  returnValue = ((endPrice - startPrice) / startPrice) * 100;
                } else {
                  returnValue = null;
                }
              }
            } catch (err) {
              console.error(`無法計算 ${benchmark.ticker} 的報酬:`, err);
              returnValue = null;
            }

            // 更新本地集合
            collectedReturns[benchmark.id] = returnValue;

            // 每個基準完成後漸進更新狀態
            setCustomBenchmarkReturns(prev => ({
              ...prev,
              [benchmark.id]: returnValue
            }));
          })
        );

        // 儲存完整結果至快取
        saveCachedCustomBenchmarkReturns(selectedYear, collectedReturns);

      } catch (err) {
        console.error('無法計算自訂基準報酬:', err);
      } finally {
        setIsLoadingCustomBenchmark(false);
      }
    };

    fetchCustomBenchmarkReturns();
  }, [selectedYear, availableYears, customBenchmarks, selectedBenchmarks]);

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
        // 檢查是否為 Euronext ticker (market = 4)
        if (mp.market === 4) {
          const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(mp.ticker, homeCurrency);
          if (euronextQuote?.exchangeRate) {
            prices[mp.ticker] = {
              price: euronextQuote.price,
              exchangeRate: euronextQuote.exchangeRate,
            };
            // 儲存至快取供未來使用
            saveQuoteToCache(mp.ticker, euronextQuote.price, euronextQuote.exchangeRate);
          }
          return;
        }

        // 標準市場處理
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
          // 儲存至快取供未來使用
          saveQuoteToCache(mp.ticker, quote.price, quote.exchangeRate);
        }
      } catch {
        // 若 US 失敗則嘗試 UK 作為備援（適用於 VWRA 等 ETF）
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
              // 儲存至快取供未來使用
              saveQuoteToCache(mp.ticker, ukQuote.price, ukQuote.exchangeRate);
            }
          } catch {
            // UK 也失敗
            console.error(`無法從 US 和 UK 市場取得 ${mp.ticker} 的價格`);
          }
        } else {
          console.error(`無法取得 ${mp.ticker} 的價格`);
        }
      }
    }));

    return prices;
  }, []);

  /**
   * 匯率查詢失敗時的 fallback（僅作最後手段）。
   *
   * 注意：這些是硬編碼估值，用於避免完全無法計算，但不保證準確。
   */
  const getFallbackExchangeRate = useCallback(async (currency: string, homeCurrency: string): Promise<number | null> => {
    const fromCur = currency.trim().toUpperCase();
    const toCur = homeCurrency.trim().toUpperCase();

    if (!fromCur || !toCur) return null;
    if (fromCur === toCur) return 1;

    // 優先嘗試透過既有 API 取得匯率（含 localStorage 快取）。
    // 若 API 失敗，才使用硬編碼 fallback（最後手段）。
    const apiRate = await stockPriceApi.getExchangeRateValue(fromCur, toCur);
    if (apiRate && apiRate > 0) return apiRate;

    if (toCur === 'TWD') {
      const toTwd: Record<string, number> = {
        'USD': 32,
        'GBP': 40,
        'EUR': 35,
        'JPY': 0.21,
      };
      return toTwd[fromCur] || null;
    }

    if (toCur === 'USD') {
      const toUsd: Record<string, number> = {
        'GBP': 1.25,
        'EUR': 1.08,
        'JPY': 0.0067,
        'TWD': 0.031,
      };
      return toUsd[fromCur] || null;
    }

    return null;
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

    // 依價格類型分類
    const yearStartMissing = missingPrices.filter(mp => mp.priceType === 'YearStart');
    const yearEndMissing = missingPrices.filter(mp => mp.priceType === 'YearEnd');

    // 收集需要查詢匯率的貨幣
    const currenciesNeeded = new Set<string>();

    // 從上一年度 12/31 取得年初價格
    // 後端同時處理國際股（Stooq）和台股（TWSE）
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

        // 取得年初的歷史匯率
        const yearStartRates: Record<string, number> = {};
        if (currenciesNeeded.size > 0 && homeCurrency === 'TWD') {
          const ratePromises = Array.from(currenciesNeeded).map(async (currency) => {
            try {
              const rate = await marketDataApi.getHistoricalExchangeRate(currency, homeCurrency, yearStartDate);
              if (rate) {
                yearStartRates[currency] = rate.rate;
              }
            } catch {
              console.warn(`無法取得 ${yearStartDate} 的 ${currency}/${homeCurrency} 匯率`);
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
              // API 失敗時使用備援匯率
              const fallbackRate = await getFallbackExchangeRate(result.currency, homeCurrency);
              exchangeRate = fallbackRate ?? 1;
            }
            yearStartPrices[mp.ticker] = {
              price: result.price,
              exchangeRate,
            };
          }
        }
      } catch (err) {
        console.error('無法從 Stooq 取得年初價格:', err);
      }
    }

    // 從當年度 12/31 取得年底價格
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

        // 取得年底的歷史匯率
        const yearEndRates: Record<string, number> = {};
        if (yearEndCurrenciesNeeded.size > 0 && homeCurrency === 'TWD') {
          const ratePromises = Array.from(yearEndCurrenciesNeeded).map(async (currency) => {
            try {
              const rate = await marketDataApi.getHistoricalExchangeRate(currency, homeCurrency, yearEndDate);
              if (rate) {
                yearEndRates[currency] = rate.rate;
              }
            } catch {
              console.warn(`無法取得 ${yearEndDate} 的 ${currency}/${homeCurrency} 匯率`);
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
              // API 失敗時使用備援匯率
              const fallbackRate = await getFallbackExchangeRate(result.currency, homeCurrency);
              exchangeRate = fallbackRate ?? 1;
            }
            yearEndPrices[mp.ticker] = {
              price: result.price,
              exchangeRate,
            };
          }
        }
      } catch (err) {
        console.error('無法從 Stooq 取得年底價格:', err);
      }
    }

    return { yearStartPrices, yearEndPrices };
  }, [getFallbackExchangeRate]);

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
        fetchRetryCountRef.current = 0; // 無缺漏價格時重設重試計數
        return;
      }
      if (hasFetchedForYearRef.current === selectedYear) return; // 該年度已抓取過

      // 限制自動重試次數以防止無限迴圈（最多 2 次）
      if (fetchRetryCountRef.current >= 2) {
        setPriceFetchFailed(true);
        return;
      }

      // 標記為抓取中（使用負數表示進行中）
      const fetchingMarker = -selectedYear;
      if (hasFetchedForYearRef.current === fetchingMarker) return; // 已在抓取中
      hasFetchedForYearRef.current = fetchingMarker;
      fetchRetryCountRef.current += 1;

      setPriceFetchFailed(false);

      try {
        const isCurrentYear = selectedYear === availableYears.currentYear;
        let allFetched = false;

        if (isCurrentYear) {
          // YTD：優先使用快取價格，再抓取剩餘的
          const tickers = performance.missingPrices.map(mp => mp.ticker);
          const cachedPrices = loadCachedPrices(tickers);

          // 若有快取價格則立即計算（不顯示 spinner）
          // 在背景抓取新鮮價格時提供即時回饋
          const cachedCount = Object.keys(cachedPrices).length;
          if (cachedCount > 0) {
            // 使用快取價格立即計算（不顯示 spinner）
            calculatePerformance(selectedYear, cachedPrices);
          }

          const stillMissing = performance.missingPrices.filter(
            mp => !cachedPrices[mp.ticker]
          );

          // 只在需要抓取價格時顯示 loading spinner
          let fetchedPrices: Record<string, YearEndPriceInfo> = {};
          if (stillMissing.length > 0) {
            setIsFetchingPrices(true);
            fetchedPrices = await fetchCurrentPrices(stillMissing, portfolio.homeCurrency);
          }

          const allPrices = { ...cachedPrices, ...fetchedPrices };
          const fetchedCount = Object.keys(allPrices).length;

          // 只在抓到新價格時再次呼叫 calculatePerformance
          // （若只有快取價格則上面已計算過）
          if (Object.keys(fetchedPrices).length > 0 && fetchedCount > 0) {
            calculatePerformance(selectedYear, allPrices);
          }

          // 檢查是否所有價格都已抓取
          allFetched = fetchedCount >= performance.missingPrices.length;
          if (!allFetched) {
            setPriceFetchFailed(true);
          }
        } else {
          // 歷史年度：使用 Stooq 取得國際股價格
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

          // 檢查是否所有價格都已抓取
          const totalFetched = Object.keys(yearEndPrices).length + Object.keys(yearStartPrices).length;
          allFetched = totalFetched >= performance.missingPrices.length;
          if (!allFetched) {
            setPriceFetchFailed(true);
          }
        }

        // 只在成功抓取所有價格時標記為已抓取
        // 若只抓到部分則允許下次 render 時重試
        if (allFetched) {
          hasFetchedForYearRef.current = selectedYear;
        } else {
          // 重設以允許重試（清除抓取中標記）
          hasFetchedForYearRef.current = null;
        }
      } catch (err) {
        console.error('自動抓取價格失敗:', err);
        setPriceFetchFailed(true);
        // 錯誤時重設以允許重試
        hasFetchedForYearRef.current = null;
      } finally {
        setIsFetchingPrices(false);
      }
    };

    autoFetchPrices();
  }, [performance, portfolio, selectedYear, availableYears, loadCachedPrices, fetchCurrentPrices, fetchHistoricalPrices, calculatePerformance]);

  // 手動重新整理按鈕處理
  const handleRefreshPrices = async () => {
    if (!performance || !portfolio || !selectedYear || !availableYears) return;
    if (performance.missingPrices.length === 0) return;

    setIsFetchingPrices(true);
    setDismissMissingPricesOverlay(false); // 手動重新整理時再次顯示 overlay
    hasFetchedForYearRef.current = null; // 重設以允許重新抓取
    fetchRetryCountRef.current = 0; // 手動重新整理時重設重試計數

    try {
      const isCurrentYear = selectedYear === availableYears.currentYear;

      if (isCurrentYear) {
        // YTD：使用 Sina/Euronext 即時 API
        const fetchedPrices = await fetchCurrentPrices(
          performance.missingPrices,
          portfolio.homeCurrency
        );
        if (Object.keys(fetchedPrices).length > 0) {
          calculatePerformance(selectedYear, fetchedPrices);
        }
      } else {
        // 歷史年度：使用 Stooq 歷史 API
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
      console.error('抓取價格失敗:', err);
    } finally {
      setIsFetchingPrices(false);
    }
  };

  const handleYearChange = (year: number) => {
    setSelectedYear(year);
    setPriceFetchFailed(false);
    setDismissMissingPricesOverlay(false); // 新年度重設 overlay dismiss 狀態
    hasFetchedForYearRef.current = null; // 重設以允許新年度重新抓取
    fetchRetryCountRef.current = 0; // 新年度重設重試計數
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

  // 注意：isLoadingPortfolio 和 !portfolio 檢查現在在父元件 PerformancePage 中

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* 頁首 */}
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

        {/* 績效卡片 */}
        {isLoadingPerformance && !performance ? (
          <div className="card-dark p-8 flex items-center justify-center">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
            <span className="ml-2 text-[var(--text-muted)]">計算績效中...</span>
          </div>
        ) : performance ? (
          <>
            {/* 背景更新時顯示小型 loading 指示器 */}
            {isLoadingPerformance && (
              <div className="fixed bottom-4 right-4 bg-[var(--bg-tertiary)] border border-[var(--border-color)] px-4 py-2 rounded-full shadow-lg flex items-center gap-2 z-50 animate-in fade-in slide-in-from-bottom-4">
                <Loader2 className="w-4 h-4 animate-spin text-[var(--accent-peach)]" />
                <span className="text-xs text-[var(--text-secondary)]">更新數據中...</span>
              </div>
            )}

            {/* 缺漏價格 Overlay - 抓取中或有缺漏價格時顯示全螢幕 modal */}
            {performance.missingPrices.length > 0 && !dismissMissingPricesOverlay && priceFetchFailed && (
              <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                <div className="card-dark w-full max-w-md mx-4">
                  <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
                    <h3 className="text-lg font-bold text-[var(--text-primary)]">
                      {isFetchingPrices ? '正在抓取價格...' : '缺少股票價格'}
                    </h3>
                    {!isFetchingPrices && (
                      <button
                        type="button"
                        onClick={() => setDismissMissingPricesOverlay(true)}
                        className="p-1 text-[var(--text-muted)] hover:text-[var(--text-primary)] rounded transition-colors"
                        title="關閉"
                      >
                        <X className="w-5 h-5" />
                      </button>
                    )}
                  </div>
                  <div className="p-5">
                    {(() => {
                      // 去重 ticker 以供顯示
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

            <div className="flex justify-end mb-4">
              <CurrencyToggle
                value={currencyMode}
                onChange={setCurrencyMode}
                sourceCurrency={performance.sourceCurrency}
                homeCurrency={portfolio.homeCurrency}
              />
            </div>

            {/* 績效指標 - 年度報酬率卡片 */}
            <div className="grid grid-cols-1 gap-6 mb-6">
              <div className={`card-dark p-6 ${currencyMode === 'source' ? 'border-l-4 border-[var(--accent-peach)]' : ''}`}>
                <div className="flex items-center gap-2 mb-4">
                  <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                  <h3 className="text-[var(--text-muted)]">
                    {selectedYear} 年度報酬 ({currencyMode === 'home' ? portfolio.homeCurrency : performance.sourceCurrency})
                  </h3>
                  <div className="relative group">
                    <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                    <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                      <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                        {currencyMode === 'source'
                          ? '原幣報酬率（不含匯率變動）'
                          : `${performance.transactionCount} 筆交易`}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {/* 資金加權報酬率 */}
                  <div>
                    <div className="flex items-center gap-1 mb-1">
                      <p className="text-sm text-[var(--text-muted)]">資金加權報酬率</p>
                      <div className="relative group">
                        <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                        <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                          <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                            衡量投資人操作 (Modified Dietz)
                          </div>
                        </div>
                      </div>
                    </div>
                    {(currencyMode === 'source'
                      ? performance.modifiedDietzPercentageSource
                      : performance.modifiedDietzPercentage) != null ? (
                      <div className="flex items-center gap-2">
                        {(currencyMode === 'source'
                          ? performance.modifiedDietzPercentageSource!
                          : performance.modifiedDietzPercentage!) >= 0 ? (
                          <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                        ) : (
                          <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                        )}
                        <span className={`text-3xl font-bold number-display ${
                          (currencyMode === 'source'
                            ? performance.modifiedDietzPercentageSource!
                            : performance.modifiedDietzPercentage!) >= 0 ? 'number-positive' : 'number-negative'
                        }`}>
                          {formatPercent(currencyMode === 'source'
                            ? performance.modifiedDietzPercentageSource
                            : performance.modifiedDietzPercentage)}
                        </span>
                      </div>
                    ) : isFetchingPrices ? (
                      <div className="flex items-center gap-2">
                        <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                        <span className="text-lg text-[var(--text-muted)]">抓取價格中...</span>
                      </div>
                    ) : (
                      <span className="text-2xl text-[var(--text-muted)]">—</span>
                    )}
                  </div>

                  {/* 時間加權報酬率 */}
                  <div>
                    <div className="flex items-center gap-1 mb-1">
                      <p className="text-sm text-[var(--text-muted)]">時間加權報酬率</p>
                      <div className="relative group">
                        <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                        <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                          <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                            衡量資產本身表現 (TWR)
                          </div>
                        </div>
                      </div>
                    </div>
                    {(currencyMode === 'source'
                      ? performance.timeWeightedReturnPercentageSource
                      : performance.timeWeightedReturnPercentage) != null ? (
                      <div className="flex items-center gap-2">
                        {(currencyMode === 'source'
                          ? performance.timeWeightedReturnPercentageSource!
                          : performance.timeWeightedReturnPercentage!) >= 0 ? (
                          <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                        ) : (
                          <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                        )}
                        <span className={`text-3xl font-bold number-display ${
                          (currencyMode === 'source'
                            ? performance.timeWeightedReturnPercentageSource!
                            : performance.timeWeightedReturnPercentage!) >= 0 ? 'number-positive' : 'number-negative'
                        }`}>
                          {formatPercent(currencyMode === 'source'
                            ? performance.timeWeightedReturnPercentageSource
                            : performance.timeWeightedReturnPercentage)}
                        </span>
                      </div>
                    ) : isFetchingPrices ? (
                      <div className="flex items-center gap-2">
                        <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                        <span className="text-lg text-[var(--text-muted)]">抓取價格中...</span>
                      </div>
                    ) : (
                      <span className="text-2xl text-[var(--text-muted)]">—</span>
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* Value Summary */}
            <div className={`card-dark p-6 mb-6 ${currencyMode === 'source' ? 'border-l-4 border-[var(--accent-peach)]' : ''}`}>
              <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                {selectedYear} 年度摘要 ({currencyMode === 'home' ? portfolio.homeCurrency : performance.sourceCurrency})
              </h3>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <div>
                  <p className="text-sm text-[var(--text-muted)]">年初價值</p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {currencyMode === 'source'
                      ? (performance.startValueSource == null || performance.startValueSource === 0
                          ? '首年'
                          : `${formatCurrency(performance.startValueSource)} ${performance.sourceCurrency}`)
                      : (performance.startValueHome == null || performance.startValueHome === 0
                          ? '首年'
                          : `${formatCurrency(performance.startValueHome)} ${portfolio.homeCurrency}`)}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">
                    {selectedYear === availableYears?.currentYear ? '目前價值' : '年底價值'}
                  </p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {currencyMode === 'source'
                      ? `${formatCurrency(performance.endValueSource)} ${performance.sourceCurrency}`
                      : `${formatCurrency(performance.endValueHome)} ${portfolio.homeCurrency}`}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">淨投入</p>
                  <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                    {currencyMode === 'source'
                      ? `${formatCurrency(performance.netContributionsSource)} ${performance.sourceCurrency}`
                      : `${formatCurrency(performance.netContributionsHome)} ${portfolio.homeCurrency}`}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-[var(--text-muted)]">淨獲利</p>
                  {(() => {
                    const profit = currencyMode === 'source'
                      ? (performance.endValueSource ?? 0) - (performance.startValueSource ?? 0) - (performance.netContributionsSource ?? 0)
                      : (performance.endValueHome ?? 0) - (performance.startValueHome ?? 0) - performance.netContributionsHome;

                    const currencyLabel = currencyMode === 'source'
                      ? performance.sourceCurrency
                      : portfolio.homeCurrency;

                    return (
                      <p className={`text-lg font-medium number-display ${profit >= 0 ? 'number-positive' : 'number-negative'}`}>
                        {formatCurrency(profit)} {currencyLabel}
                      </p>
                    );
                  })()}
                </div>
              </div>
            </div>

            {/* 績效比較長條圖 - 投資組合 vs 基準（多選） */}
            {performance && (
              <div className="card-dark p-6 mt-6">
                <div className="flex justify-between items-center mb-4">
                  <h3 className="text-lg font-bold text-[var(--text-primary)]">
                    績效比較
                  </h3>
                  <div className="flex items-center gap-3">
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
                </div>

                {/* 基準設定 Modal - Dashboard 風格 */}
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
                {/* 等待所有基準資料載入完成後才顯示長條圖，避免分批渲染閃爍 */}
                {/* 只有當所有選擇的基準都有資料時才顯示，否則等待載入 */}
                {(() => {
                  // 檢查是否所有選擇的基準都有資料
                  const systemBenchmarks = selectedBenchmarks.filter(k => !k.startsWith('custom_'));
                  const allSystemBenchmarksReady = systemBenchmarks.length === 0 ||
                    systemBenchmarks.every(k => benchmarkReturns[k] !== undefined);

                  const selectedCustomBenchmarkIds = customBenchmarks
                    .filter(b => selectedBenchmarks.includes(`custom_${b.id}`))
                    .map(b => b.id);
                  const allCustomBenchmarksReady = selectedCustomBenchmarkIds.length === 0 ||
                    selectedCustomBenchmarkIds.every(id => customBenchmarkReturns[id] !== undefined);

                  // 需要等待所有資料都準備好
                  const allDataReady = allSystemBenchmarksReady && allCustomBenchmarksReady;

                  if (!allDataReady) {
                    return (
                      <div className="flex items-center justify-center py-8">
                        <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                        <span className="ml-2 text-[var(--text-muted)]">載入基準報酬中...</span>
                      </div>
                    );
                  }

                  return (
                    <>
                      <PerformanceBarChart
                      data={[
                        // Only include portfolio return if value exists
                        ...((currencyMode === 'home'
                          ? performance.modifiedDietzPercentage
                          : performance.modifiedDietzPercentageSource) != null
                          ? [{
                              label: `我的投資組合 (${currencyMode === 'home' ? portfolio.homeCurrency : performance.sourceCurrency})`,
                              value: currencyMode === 'home'
                                ? performance.modifiedDietzPercentage!
                                : performance.modifiedDietzPercentageSource!,
                              tooltip: `${selectedYear} 年度報酬率（資金加權報酬率 / Modified Dietz）`,
                            }]
                          : []),
                        /* FR-134: 過濾掉資料為 null 的基準，不顯示為 0 */
                        ...selectedBenchmarks
                          .filter(benchmarkKey => benchmarkReturns[benchmarkKey] != null)
                          .map(benchmarkKey => {
                            const benchmarkInfo = BENCHMARK_OPTIONS.find(b => b.key === benchmarkKey);
                            const returnValue = benchmarkReturns[benchmarkKey]!;
                            const source = benchmarkReturnSources[benchmarkKey];

                            return {
                              label: benchmarkInfo?.label ?? benchmarkKey,
                              value: returnValue,
                              tooltip: `${selectedYear} 年度報酬率${source ? `（來源：${source}）` : ''}`,
                            };
                          }),
                        /* 自訂使用者基準 - 僅顯示已選擇的 */
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
                    {/* FR-134: 顯示因資料不可用而隱藏的基準 */}
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
                  );
                })()}
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

        {/* 缺漏價格 Modal */}
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
