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
import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Loader2, TrendingUp, TrendingDown, Calendar, RefreshCw, Info, Settings, X, Check } from 'lucide-react';
import { DEFAULT_BENCHMARKS } from '../constants';
import { stockPriceApi, marketDataApi, userBenchmarkApi, userPreferencesApi } from '../services/api';
import { loadCachedYtdData, getYtdData, transformYtdData } from '../services/ytdApi';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { usePortfolio } from '../contexts/PortfolioContext';
import { PortfolioSelector } from '../components/portfolio/PortfolioSelector';
import { YearSelector } from '../components/performance/YearSelector';
import { CurrencyToggle, type PerformanceCurrencyMode } from '../components/performance/CurrencyToggle';
import { MissingPriceModal, type MissingPriceSubmissionPayload } from '../components/modals/MissingPriceModal';
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
const MINIMUM_RELIABLE_COVERAGE_DAYS = 90;

const RETURN_DISPLAY_DEGRADE_REASON_COPY: Record<string, string> = {
  LOW_CONFIDENCE_NO_OPENING_BASELINE: '此年度缺少期初基準，資金加權報酬率（MD）信度偏低。',
  LOW_CONFIDENCE_LOW_COVERAGE: '此年度資料覆蓋天數不足，資金加權報酬率（MD）信度偏低。',
  LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE: '此年度同時缺少期初基準且資料覆蓋不足，資金加權報酬率（MD）信度偏低。',
};

const PERFORMANCE_LOADING_STAGE_FLOW_COPY = '準備資料 → 補齊價格 → 計算中 → 較久等待提醒';

interface PerformanceLoadingStageFeedback {
  currentStage: string;
  hint: string;
}

function getPerformanceLoadingStageFeedback({
  elapsedMs,
  selectedYear,
  currentYear,
  hasMissingPrices,
  isFetchingPrices,
}: {
  elapsedMs: number;
  selectedYear: number | null;
  currentYear: number | null;
  hasMissingPrices: boolean;
  isFetchingPrices: boolean;
}): PerformanceLoadingStageFeedback {
  const safeElapsedMs = Math.max(0, elapsedMs);
  const yearLabel = selectedYear != null ? `${selectedYear} 年` : '本年度';
  const isCurrentYear = selectedYear != null && currentYear != null && selectedYear === currentYear;

  if (safeElapsedMs < 2500) {
    return {
      currentStage: '準備資料',
      hint: `正在整理 ${yearLabel} 績效資料與快取結果。`,
    };
  }

  if (isFetchingPrices || hasMissingPrices) {
    if (safeElapsedMs < 8000) {
      return {
        currentStage: '補齊價格',
        hint: isCurrentYear
          ? '偵測到缺漏價格，正在補抓即時報價後重新計算。'
          : `偵測到缺漏價格，正在補抓 ${yearLabel} 歷史價格後重新計算。`,
      };
    }
  }

  if (safeElapsedMs < 15000) {
    return {
      currentStage: '計算中',
      hint: '正在重算報酬率與年度摘要，完成後會自動更新畫面。',
    };
  }

  return {
    currentStage: '較久等待提醒',
    hint: '這次等待時間較長，可能是資料量較多或外部價格來源較慢，請稍候。',
  };
}

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

// 報價快取 key：舊版（ticker-only）與新版（ticker+market）
const getLegacyQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;
const getMarketAwareQuoteCacheKey = (ticker: string, market?: StockMarketType) =>
  `quote_cache_${ticker}_${market ?? 'default'}`;

const getMissingPriceKey = (missingPrice: MissingPrice) =>
  `${missingPrice.ticker}::${missingPrice.priceType}`;

const dedupeMissingPricesByKey = (missingPrices: MissingPrice[]): MissingPrice[] => {
  const seen = new Set<string>();

  return missingPrices.filter((missingPrice) => {
    const key = getMissingPriceKey(missingPrice);
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
};

interface QuoteCachePayload {
  quote?: {
    price?: number;
    exchangeRate?: number;
  };
}

/**
 * 讀取報價快取時，優先市場化 key，再回退 legacy key。
 */
function loadQuoteFromCache(ticker: string, market?: StockMarketType): QuoteCachePayload | null {
  const candidateKeys = [
    getMarketAwareQuoteCacheKey(ticker, market),
    getMarketAwareQuoteCacheKey(ticker),
    getLegacyQuoteCacheKey(ticker),
  ];

  for (const key of new Set(candidateKeys)) {
    try {
      const cached = localStorage.getItem(key);
      if (!cached) continue;

      return JSON.parse(cached) as QuoteCachePayload;
    } catch {
      // Ignore broken cache entry and continue fallback chain
    }
  }

  return null;
}

/**
 * 儲存報價至 localStorage 快取。
 *
 * - 新版寫入 market-aware key（避免跨市場污染）
 * - 同步寫入 legacy key（維持向後相容）
 */
function saveQuoteToCache(
  ticker: string,
  price: number,
  exchangeRate: number,
  market?: StockMarketType,
): void {
  try {
    const resolvedMarket = market ?? guessMarket(ticker);
    const marketAwareCacheData = {
      quote: { price, exchangeRate },
      updatedAt: new Date().toISOString(),
      market: resolvedMarket,
    };
    const legacyCacheData = {
      quote: { price, exchangeRate },
      timestamp: Date.now(),
    };

    localStorage.setItem(
      getMarketAwareQuoteCacheKey(ticker, resolvedMarket),
      JSON.stringify(marketAwareCacheData),
    );
    localStorage.setItem(getLegacyQuoteCacheKey(ticker), JSON.stringify(legacyCacheData));
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
const STOCK_MARKET_VALUE_BY_NAME: Record<string, StockMarketType> = {
  TW: StockMarket.TW,
  US: StockMarket.US,
  UK: StockMarket.UK,
  EU: StockMarket.EU,
};

function normalizeMarketValue(market: unknown): StockMarketType {
  if (typeof market === 'number' && Number.isFinite(market)) {
    return market as StockMarketType;
  }

  if (typeof market === 'string') {
    const normalized = market.trim().toUpperCase();
    if (/^\d+$/.test(normalized)) {
      return Number(normalized) as StockMarketType;
    }

    return STOCK_MARKET_VALUE_BY_NAME[normalized] ?? StockMarket.US;
  }

  return StockMarket.US;
}

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
      const parsed = JSON.parse(cached) as UserBenchmark[];
      if (!Array.isArray(parsed)) return [];

      return parsed.map((benchmark) => ({
        ...benchmark,
        market: normalizeMarketValue(benchmark.market),
      }));
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
  const navigate = useNavigate();

  // 使用共用的投資組合 context（與 Portfolio 頁面同步）
  const {
    currentPortfolio: portfolio,
    isAllPortfolios,
    isLoading: isLoadingPortfolio,
    performanceVersion,
  } = usePortfolio();

  const handleCreatePortfolio = useCallback(() => {
    navigate('/portfolio');
  }, [navigate]);

  if (isLoadingPortfolio) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--accent-peach)]" />
      </div>
    );
  }

  // 所有投資組合模式沿用同一套 UI，但改走 aggregate API
  if (isAllPortfolios) {
    return (
      <PerformancePageContent
        key={`aggregate-${performanceVersion}`}
        onCreatePortfolio={handleCreatePortfolio}
        isAggregate
      />
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
      onCreatePortfolio={handleCreatePortfolio}
      isAggregate={false}
    />
  );
}

// 內部元件：
// - 單一投資組合模式：使用 portfolio
// - 彙總模式：isAggregate=true，不依賴單一 portfolio
function PerformancePageContent({
  portfolio,
  onCreatePortfolio,
  isAggregate = false,
}: {
  portfolio?: NonNullable<ReturnType<typeof usePortfolio>['currentPortfolio']>;
  onCreatePortfolio: () => void;
  isAggregate?: boolean;
}) {
  const [showMissingPriceModal, setShowMissingPriceModal] = useState(false);
  const [isFetchingPrices, setIsFetchingPrices] = useState(false);
  const [priceFetchFailed, setPriceFetchFailed] = useState(false);
  const [dismissMissingPricesOverlay, setDismissMissingPricesOverlay] = useState(false);
  const [performanceLoadingElapsedMs, setPerformanceLoadingElapsedMs] = useState(0);
  const hasFetchedForYearRef = useRef<number | null>(null);
  const fetchRetryCountRef = useRef<number>(0); // 限制自動重試次數以防止無限迴圈
  const benchmarkRequestIdRef = useRef(0);
  const customBenchmarkRequestIdRef = useRef(0);
  const autoFetchPricesRequestIdRef = useRef(0);
  const performanceLoadingStartedAtRef = useRef<number | null>(null);
  const performanceLoadingYearRef = useRef<number | null>(null);

  const displayHomeCurrency = portfolio?.homeCurrency ?? 'TWD';

  // 基準比較狀態 - 支援多選，與 Dashboard 同步
  const [selectedBenchmarks, setSelectedBenchmarks] = useState<string[]>(loadSelectedBenchmarksFromLocalStorage);
  const [tempSelectedBenchmarks, setTempSelectedBenchmarks] = useState<string[]>([]);
  const [showBenchmarkSettings, setShowBenchmarkSettings] = useState(false);

  // 從快取 lazy init ytdData 以即時顯示
  const [ytdData, setYtdData] = useState<MarketYtdComparison | null>(() => {
    const cached = loadCachedYtdData();
    return cached.data ? transformYtdData(cached.data) : null;
  });
  // 記錄 YTD 載入是否已完成（成功或失敗）
  // 用於當年度 YTD 不可用時啟用 benchmark returns API 備援，避免 loading 卡住
  const [isYtdLoadSettled, setIsYtdLoadSettled] = useState(() => {
    const cached = loadCachedYtdData();
    const currentYear = new Date().getFullYear();
    return Boolean(cached.data && cached.data.year === currentYear);
  });

  // 從 YTD 快取（當年度）或系統基準快取（歷史年度）lazy init benchmarkReturns
  // 避免只有投資組合長條先顯示，基準稍後才出現的閃爍問題
  const [benchmarkReturns, setBenchmarkReturns] = useState<Record<string, number | null>>(() => {
    const currentYear = new Date().getFullYear();
    const savedBenchmarks = loadSelectedBenchmarksFromLocalStorage();
    const systemBenchmarks = savedBenchmarks.filter(k => !k.startsWith('custom_'));

    // 優先從 YTD 快取載入（當年度）
    const cachedYtd = loadCachedYtdData();
    if (cachedYtd.data && cachedYtd.data.year === currentYear) {
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
  // 追蹤每個系統 benchmark 報酬值對應的年份，避免 selectedYear 與資料年份錯位
  const [benchmarkReturnYears, setBenchmarkReturnYears] = useState<Record<string, number>>(() => {
    const currentYear = new Date().getFullYear();
    const savedBenchmarks = loadSelectedBenchmarksFromLocalStorage();
    const systemBenchmarks = savedBenchmarks.filter(k => !k.startsWith('custom_'));

    if (systemBenchmarks.length === 0) {
      return {};
    }

    const cachedYtd = loadCachedYtdData();
    if (cachedYtd.data && cachedYtd.data.year === currentYear) {
      const years: Record<string, number> = {};
      for (const key of systemBenchmarks) {
        years[key] = currentYear;
      }
      return years;
    }

    const systemCache = loadCachedSystemBenchmarkReturns(currentYear);
    if (Object.keys(systemCache.returns).length > 0) {
      const years: Record<string, number> = {};
      for (const key of systemBenchmarks) {
        years[key] = currentYear;
      }
      return years;
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
    const currentYear = new Date().getFullYear();
    // 檢查 YTD 快取（當年度主要來源）
    const cachedYtd = loadCachedYtdData();
    if (cachedYtd.data && cachedYtd.data.year === currentYear) {
      return false; // 有當年度 YTD 快取，不需要 loading
    }
    // 備援：檢查系統基準快取
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
  const effectiveCurrencyMode: PerformanceCurrencyMode = isAggregate ? 'home' : currencyMode;

  const handleCurrencyModeChange = useCallback((mode: PerformanceCurrencyMode) => {
    setCurrencyMode(isAggregate ? 'home' : mode);
  }, [isAggregate]);

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
  const [customBenchmarkReturnYears, setCustomBenchmarkReturnYears] = useState<Record<string, number>>(() => {
    const currentYear = new Date().getFullYear();
    const cachedReturns = loadCachedCustomBenchmarkReturns(currentYear);
    const years: Record<string, number> = {};

    for (const benchmarkId of Object.keys(cachedReturns)) {
      years[benchmarkId] = currentYear;
    }

    return years;
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
    portfolioId: portfolio?.id ?? 'aggregate',
    isAggregate,
    autoFetch: true,
  });

  // 元件掛載時重設報價抓取狀態（portfolio 透過 key 變更時會重新掛載）
  // 無需依賴項 - 當 portfolio 變更時元件會因 key prop 重新掛載
  // 注意：不清空 benchmarkReturns，因為 lazy init 已載入當年度快取
  useEffect(() => {
    hasFetchedForYearRef.current = null;
    fetchRetryCountRef.current = 0;
    autoFetchPricesRequestIdRef.current += 1;
    setPriceFetchFailed(false);
    setIsFetchingPrices(false);
  }, []);

  // 追蹤績效主區塊 loading 時間，提供分段等待回饋文案
  useEffect(() => {
    if (isLoadingPerformance) {
      const hasNewLoadingCycle =
        performanceLoadingStartedAtRef.current == null ||
        performanceLoadingYearRef.current !== selectedYear;

      if (hasNewLoadingCycle) {
        performanceLoadingStartedAtRef.current = Date.now();
        performanceLoadingYearRef.current = selectedYear;
        setPerformanceLoadingElapsedMs(0);
      }

      const tick = () => {
        if (performanceLoadingStartedAtRef.current == null) {
          return;
        }

        setPerformanceLoadingElapsedMs(Date.now() - performanceLoadingStartedAtRef.current);
      };

      tick();
      const timer = window.setInterval(tick, 1000);

      return () => {
        window.clearInterval(timer);
      };
    }

    performanceLoadingStartedAtRef.current = null;
    performanceLoadingYearRef.current = null;
    setPerformanceLoadingElapsedMs(0);

    return undefined;
  }, [isLoadingPerformance, selectedYear]);

  // 載入當年度 YTD 基準資料
  // 快取已透過 lazy init 載入，這裡只在背景抓取最新資料
  useEffect(() => {
    const loadFreshYtdData = async () => {
      try {
        const data = await getYtdData();
        setYtdData(transformYtdData(data));
      } catch (err) {
        console.error('無法載入 YTD 資料:', err);
      } finally {
        setIsYtdLoadSettled(true);
      }
    };
    loadFreshYtdData();
  }, []);

  // 載入使用者的自訂基準
  useEffect(() => {
    const loadCustomBenchmarks = async () => {
      try {
        const benchmarks = await userBenchmarkApi.getAll();
        const normalizedBenchmarks = benchmarks.map((benchmark) => ({
          ...benchmark,
          market: normalizeMarketValue(benchmark.market),
        }));
        setCustomBenchmarks(normalizedBenchmarks);
        // 快取以便下次載入時即時顯示
        saveCachedCustomBenchmarks(normalizedBenchmarks);

        // 清理已刪除的自訂基準：從 selectedBenchmarks 中移除不存在的 custom_ key
        const validCustomKeys = new Set(normalizedBenchmarks.map(b => `custom_${b.id}`));
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

  // aggregate 模式下做一次以 ticker + priceType 為鍵的去重，避免跨投組重複項目。
  // 單一投資組合模式保留原始資料。
  const effectiveMissingPrices = useMemo(() => {
    if (!performance) {
      return [];
    }

    if (!isAggregate) {
      return performance.missingPrices;
    }

    return dedupeMissingPricesByKey(performance.missingPrices);
  }, [performance, isAggregate]);

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
    const requestId = ++benchmarkRequestIdRef.current;
    const isLatestRequest = () => benchmarkRequestIdRef.current === requestId;

    const fetchBenchmarkReturns = async () => {
      if (!selectedYear || !availableYears) {
        // 在取得必要資料前保持 loading 狀態
        return;
      }

      const selectedSystemBenchmarks = selectedBenchmarks.filter(k => !k.startsWith('custom_'));
      if (selectedSystemBenchmarks.length === 0) {
        if (isLatestRequest()) {
          setIsLoadingBenchmark(false);
        }
        return;
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;
      const hasCurrentYearYtd = Boolean(ytdData && ytdData.year === selectedYear);

      // 當年度需等待 YTD 載入完成後再繼續
      // 若 YTD 失敗（isYtdLoadSettled=true 且當年度 ytdData 不可用），改走 benchmark returns API 備援
      if (isCurrentYear && !hasCurrentYearYtd && !isYtdLoadSettled) {
        // 保持 loading 狀態 - ytdData 或載入完成狀態更新時會重新執行
        if (isLatestRequest()) {
          setIsLoadingBenchmark(true);
        }
        return;
      }

      // 歷史年度優先嘗試從快取載入以即時顯示
      // 當年度則檢查 YTD 快取是否已包含選擇的基準
      let hasCachedData = false;
      const cachedReturnsForSelection: Record<string, number | null> = {};
      const cachedSourcesForSelection: Record<string, string | null> = {};
      const cachedYearsForSelection: Record<string, number> = {};

      if (!isCurrentYear) {
        const cached = loadCachedSystemBenchmarkReturns(selectedYear);
        if (Object.keys(cached.returns).length > 0) {
          for (const key of selectedSystemBenchmarks) {
            if (cached.returns[key] !== undefined) {
              cachedReturnsForSelection[key] = cached.returns[key] ?? null;
              cachedSourcesForSelection[key] = cached.sources[key] ?? null;
              cachedYearsForSelection[key] = selectedYear;
            }
          }

          // 檢查所有選擇的系統基準是否都在快取中
          hasCachedData = selectedSystemBenchmarks.every(k => cached.returns[k] !== undefined);
        }
      } else {
        // 當年度：僅接受「當年度」YTD 快取，避免沿用舊年份訊號
        const cachedYtd = loadCachedYtdData();
        if (cachedYtd.data && cachedYtd.data.year === selectedYear) {
          const ytd = transformYtdData(cachedYtd.data);

          for (const key of selectedSystemBenchmarks) {
            const benchmark = ytd.benchmarks.find(b => b.marketKey === key);
            if (benchmark) {
              cachedReturnsForSelection[key] = benchmark.ytdReturnPercent ?? null;
              cachedSourcesForSelection[key] = 'Calculated';
              cachedYearsForSelection[key] = selectedYear;
            }
          }

          hasCachedData = selectedSystemBenchmarks.every(k => ytd.benchmarks.some(b => b.marketKey === k));
        }
      }

      if (Object.keys(cachedReturnsForSelection).length > 0 && isLatestRequest()) {
        setBenchmarkReturns(prev => ({ ...prev, ...cachedReturnsForSelection }));
        setBenchmarkReturnSources(prev => ({ ...prev, ...cachedSourcesForSelection }));
        setBenchmarkReturnYears(prev => ({ ...prev, ...cachedYearsForSelection }));
      }

      // 只在沒有快取資料時才顯示 loading，否則在背景更新
      if (!hasCachedData) {
        if (isLatestRequest()) {
          setIsLoadingBenchmark(true);
        }
      } else if (isLatestRequest()) {
        // 有快取資料時確保 loading 為 false（避免初始值問題）
        setIsLoadingBenchmark(false);
      }
      // 不清空舊值以防止閃爍 (FR-095)

      try {
        const newReturns: Record<string, number | null> = {};
        const newSources: Record<string, string | null> = {};
        const newReturnYears: Record<string, number> = {};

        if (isCurrentYear && hasCurrentYearYtd && ytdData) {
          // 當年度使用 YTD 資料 - 以英文 key 查詢（與後端一致）
          for (const benchmarkKey of selectedSystemBenchmarks) {
            const benchmark = ytdData.benchmarks.find(b => b.marketKey === benchmarkKey);
            if (benchmark?.ytdReturnPercent != null) {
              newReturns[benchmarkKey] = benchmark.ytdReturnPercent;
              newSources[benchmarkKey] = 'Calculated';
            } else {
              newReturns[benchmarkKey] = null;
              newSources[benchmarkKey] = null;
            }
            newReturnYears[benchmarkKey] = selectedYear;
          }
        } else {
          // 歷史年度使用快取的基準報酬（優先 Yahoo Total Return，備援為計算值）
          try {
            const benchmarkData = await marketDataApi.getBenchmarkReturns(selectedYear);
            for (const benchmarkKey of selectedSystemBenchmarks) {
              const returnValue = benchmarkData.returns[benchmarkKey];
              newReturns[benchmarkKey] = returnValue ?? null;
              newSources[benchmarkKey] = benchmarkData.dataSources?.[benchmarkKey] ?? null;
              newReturnYears[benchmarkKey] = selectedYear;
            }
            // 儲存至快取供未來使用
            saveCachedSystemBenchmarkReturns(selectedYear, newReturns, newSources);
          } catch {
            // API 失敗時所有基準為 null
            for (const benchmarkKey of selectedSystemBenchmarks) {
              newReturns[benchmarkKey] = null;
              newSources[benchmarkKey] = null;
              newReturnYears[benchmarkKey] = selectedYear;
            }
          }
        }

        if (!isLatestRequest()) {
          return;
        }

        setBenchmarkReturns(prev => ({ ...prev, ...newReturns }));
        setBenchmarkReturnSources(prev => ({ ...prev, ...newSources }));
        setBenchmarkReturnYears(prev => ({ ...prev, ...newReturnYears }));
      } catch (err) {
        console.error('無法抓取基準報酬:', err);
      } finally {
        if (isLatestRequest()) {
          setIsLoadingBenchmark(false);
        }
      }
    };

    fetchBenchmarkReturns();
  }, [selectedYear, selectedBenchmarks, availableYears, ytdData, isYtdLoadSettled]);

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
    const requestId = ++customBenchmarkRequestIdRef.current;
    const isLatestRequest = () => customBenchmarkRequestIdRef.current === requestId;

    const fetchCustomBenchmarkReturns = async () => {
      if (!selectedYear || !availableYears || customBenchmarks.length === 0) {
        if (isLatestRequest()) {
          setCustomBenchmarkReturns({});
          setCustomBenchmarkReturnYears({});
          setIsLoadingCustomBenchmark(false);
        }
        return;
      }

      // 1. 優先嘗試從快取載入以即時回饋
      const cachedReturns = loadCachedCustomBenchmarkReturns(selectedYear);
      const selectedCustomBenchmarks = customBenchmarks.filter(b => selectedBenchmarks.includes(`custom_${b.id}`));

      // 若沒有選擇任何自訂基準，直接完成
      if (selectedCustomBenchmarks.length === 0) {
        if (isLatestRequest()) {
          setIsLoadingCustomBenchmark(false);
        }
        return;
      }

      // 檢查是否有足夠的快取資料
      let hasCachedData = false;
      if (Object.keys(cachedReturns).length > 0) {
        const cachedReturnsForSelection: Record<string, number | null> = {};
        const cachedYearsForSelection: Record<string, number> = {};

        for (const benchmark of selectedCustomBenchmarks) {
          if (cachedReturns[benchmark.id] !== undefined) {
            cachedReturnsForSelection[benchmark.id] = cachedReturns[benchmark.id] ?? null;
            cachedYearsForSelection[benchmark.id] = selectedYear;
          }
        }

        if (Object.keys(cachedReturnsForSelection).length > 0 && isLatestRequest()) {
          setCustomBenchmarkReturns(prev => ({ ...prev, ...cachedReturnsForSelection }));
          setCustomBenchmarkReturnYears(prev => ({ ...prev, ...cachedYearsForSelection }));
        }

        // 歷史年度（非當年度）可完全信任快取（若完整）
        const isCurrentYear = selectedYear === availableYears.currentYear;
        if (!isCurrentYear) {
           // 檢查所有選擇的基準是否都在快取中
           const allCached = selectedCustomBenchmarks.every(b => cachedReturns[b.id] !== undefined);

           if (allCached) {
             if (isLatestRequest()) {
               setIsLoadingCustomBenchmark(false);
             }
             return; // 歷史年度若快取存在則跳過網路請求
           }
        }

        // 當年度：檢查是否有任何快取資料可先顯示
        hasCachedData = selectedCustomBenchmarks.some(b => cachedReturns[b.id] !== undefined);
      }

      // 只在沒有快取資料時才顯示 loading，否則在背景更新
      if (!hasCachedData && selectedCustomBenchmarks.length > 0) {
        if (isLatestRequest()) {
          setIsLoadingCustomBenchmark(true);
        }
      } else if (isLatestRequest()) {
        // 有快取資料或沒有選擇自訂基準時，確保 loading 為 false
        setIsLoadingCustomBenchmark(false);
      }

      const isCurrentYear = selectedYear === availableYears.currentYear;

      try {
        // 追蹤收集到的報酬以便稍後儲存至快取
        const collectedReturns: Record<string, number | null> = { ...cachedReturns };

        await Promise.all(
          selectedCustomBenchmarks.map(async (benchmark) => {
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
                  // 當年度優先嘗試從快取載入以即時顯示（優先 market-aware key）
                  const cachedData = loadQuoteFromCache(benchmark.ticker, benchmark.market);
                  if (cachedData?.quote?.price) {
                    endPrice = cachedData.quote.price;
                  }

                  // 若無快取則抓取即時報價
                  if (!endPrice) {
                    try {
                      // EU 市場 (4) 使用 Euronext API
                      if (benchmark.market === 4) {
                        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                        endPrice = euronextQuote?.price;
                        if (euronextQuote?.price && euronextQuote?.exchangeRate) {
                          saveQuoteToCache(benchmark.ticker, euronextQuote.price, euronextQuote.exchangeRate, benchmark.market);
                        }
                      } else {
                        const quote = await stockPriceApi.getQuote(benchmark.market, benchmark.ticker);
                        endPrice = quote?.price;
                        // 注意：此處 quote 不含真實匯率，避免寫入共享 quote cache 汙染後續匯率換算
                        // 仍使用即時價格計算自訂 benchmark 報酬，不改變既有 UI 行為
                      }
                    } catch {
                      // 備援：若標準市場失敗則嘗試 Euronext
                      if (benchmark.market === 4) {
                        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(benchmark.ticker, 'TWD');
                        endPrice = euronextQuote?.price;
                        if (euronextQuote?.price && euronextQuote?.exchangeRate) {
                          saveQuoteToCache(benchmark.ticker, euronextQuote.price, euronextQuote.exchangeRate, benchmark.market);
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

            if (!isLatestRequest()) {
              return;
            }

            // 每個基準完成後漸進更新狀態
            setCustomBenchmarkReturns(prev => ({
              ...prev,
              [benchmark.id]: returnValue
            }));
            setCustomBenchmarkReturnYears(prev => ({
              ...prev,
              [benchmark.id]: selectedYear,
            }));
          })
        );

        if (!isLatestRequest()) {
          return;
        }

        // 儲存完整結果至快取
        saveCachedCustomBenchmarkReturns(selectedYear, collectedReturns);

      } catch (err) {
        console.error('無法計算自訂基準報酬:', err);
      } finally {
        if (isLatestRequest()) {
          setIsLoadingCustomBenchmark(false);
        }
      }
    };

    fetchCustomBenchmarkReturns();
  }, [selectedYear, availableYears, customBenchmarks, selectedBenchmarks]);

  /**
   * 從 localStorage 載入報價快取（與 Portfolio/Dashboard 共用 quote cache）。
   *
   * 使用時機：
   * - 在補齊缺漏價格前，先用快取減少 API 呼叫與等待。
   *
   * 規則：
   * - 優先使用 market-aware key（quote_cache_${ticker}_${market}）。
   * - 若找不到則回退 legacy key（quote_cache_${ticker}）。
   */
  const loadCachedPrices = useCallback((missingPrices: MissingPrice[]): Record<string, YearEndPriceInfo> => {
    const prices: Record<string, YearEndPriceInfo> = {};

    for (const mp of missingPrices) {
      const market = mp.market ?? guessMarket(mp.ticker);
      const cachedData = loadQuoteFromCache(mp.ticker, market);
      if (cachedData?.quote?.price && cachedData.quote?.exchangeRate) {
        prices[mp.ticker] = {
          price: cachedData.quote.price,
          exchangeRate: cachedData.quote.exchangeRate,
        };
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

    // 先以 ticker 去重，避免同一輪重複請求同一檔標的
    // market 以第一筆為準，符合既有資料結構（同 ticker 應屬同市場）
    const uniqueMissingPricesByTicker = missingPrices.filter((mp, index, arr) =>
      arr.findIndex(item => item.ticker === mp.ticker) === index
    );

    await Promise.all(uniqueMissingPricesByTicker.map(async (mp) => {
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
            saveQuoteToCache(mp.ticker, euronextQuote.price, euronextQuote.exchangeRate, StockMarket.EU);
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
          saveQuoteToCache(mp.ticker, quote.price, quote.exchangeRate, market);
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
              saveQuoteToCache(mp.ticker, ukQuote.price, ukQuote.exchangeRate, StockMarket.UK);
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
   * - `marketDataApi.getHistoricalPrices`：透過 backend 取得歷史收盤價（國際股採 Yahoo 優先，僅 US/UK 失敗時才回退 Stooq；台股用 TWSE）。
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
      if (!performance || !selectedYear || !availableYears) return;
      if (effectiveMissingPrices.length === 0) {
        fetchRetryCountRef.current = 0; // 無缺漏價格時重設重試計數
        return;
      }
      if (hasFetchedForYearRef.current === selectedYear) return; // 該年度已抓取過

      // 標記為抓取中（使用負數表示進行中）
      const fetchingMarker = -selectedYear;
      if (hasFetchedForYearRef.current === fetchingMarker) return; // 已在抓取中

      const requestId = ++autoFetchPricesRequestIdRef.current;
      const isLatestRequest = () => autoFetchPricesRequestIdRef.current === requestId;

      // 限制自動重試次數以防止無限迴圈（最多 2 次）
      if (fetchRetryCountRef.current >= 2) {
        if (isLatestRequest()) {
          setPriceFetchFailed(true);
        }
        return;
      }

      hasFetchedForYearRef.current = fetchingMarker;
      fetchRetryCountRef.current += 1;

      if (isLatestRequest()) {
        setPriceFetchFailed(false);
      }

      try {
        const isCurrentYear = selectedYear === availableYears.currentYear;
        let allFetched = false;

        if (isCurrentYear) {
          // YTD：優先使用快取價格，再抓取剩餘的
          // 以 ticker + priceType 去重，避免同 ticker 的 YearStart/YearEnd 被合併
          const uniqueMissingPrices = dedupeMissingPricesByKey(effectiveMissingPrices);
          const cachedPrices = loadCachedPrices(uniqueMissingPrices);

          // 若有快取價格則立即計算（不顯示 spinner）
          // 在背景抓取新鮮價格時提供即時回饋
          const cachedCount = Object.keys(cachedPrices).length;
          if (cachedCount > 0) {
            // 使用快取價格立即計算（不顯示 spinner）
            calculatePerformance(selectedYear, cachedPrices);
          }

          const stillMissing = uniqueMissingPrices.filter(
            mp => !cachedPrices[mp.ticker]
          );

          // 只在需要抓取價格時顯示 loading spinner
          let fetchedPrices: Record<string, YearEndPriceInfo> = {};
          if (stillMissing.length > 0) {
            if (isLatestRequest()) {
              setIsFetchingPrices(true);
            }
            fetchedPrices = await fetchCurrentPrices(stillMissing, displayHomeCurrency);

            if (!isLatestRequest()) {
              return;
            }
          }

          const allPrices = { ...cachedPrices, ...fetchedPrices };
          const fetchedCount = Object.keys(allPrices).length;

          // 只在抓到新價格時再次呼叫 calculatePerformance
          // （若只有快取價格則上面已計算過）
          if (Object.keys(fetchedPrices).length > 0 && fetchedCount > 0) {
            calculatePerformance(selectedYear, allPrices);
          }

          // 檢查是否所有價格都已抓取（以 ticker + priceType 鍵判定）
          const fetchedKeyCount = uniqueMissingPrices.filter(
            mp => mp.priceType === 'YearEnd' && Boolean(allPrices[mp.ticker])
          ).length;
          allFetched = fetchedKeyCount >= uniqueMissingPrices.length;
          if (!allFetched && isLatestRequest()) {
            setPriceFetchFailed(true);
          }
        } else {
          // 歷史年度：使用 Stooq 取得國際股價格
          if (isLatestRequest()) {
            setIsFetchingPrices(true);
          }
          const { yearStartPrices, yearEndPrices } = await fetchHistoricalPrices(
            effectiveMissingPrices,
            selectedYear,
            displayHomeCurrency
          );

          if (!isLatestRequest()) {
            return;
          }

          const hasPrices = Object.keys(yearEndPrices).length > 0 || Object.keys(yearStartPrices).length > 0;
          if (hasPrices) {
            calculatePerformance(selectedYear, yearEndPrices, yearStartPrices);
          }

          // 檢查是否所有價格都已抓取（以 ticker + priceType 鍵判定）
          const fetchedKeyCount = effectiveMissingPrices.filter((missingPrice) => {
            if (missingPrice.priceType === 'YearStart') {
              return Boolean(yearStartPrices[missingPrice.ticker]);
            }

            return Boolean(yearEndPrices[missingPrice.ticker]);
          }).length;
          allFetched = fetchedKeyCount >= effectiveMissingPrices.length;
          if (!allFetched && isLatestRequest()) {
            setPriceFetchFailed(true);
          }
        }

        if (!isLatestRequest()) {
          return;
        }

        // 只在成功抓取所有價格時標記為已抓取
        // 若只抓到部分則允許下次 render 時重試
        if (allFetched) {
          hasFetchedForYearRef.current = selectedYear;
        } else {
          hasFetchedForYearRef.current = null;
        }
      } catch (err) {
        if (!isLatestRequest()) {
          return;
        }

        console.error('自動抓取價格失敗:', err);
        setPriceFetchFailed(true);
        // 錯誤時重設以允許重試
        hasFetchedForYearRef.current = null;
      } finally {
        if (isLatestRequest()) {
          setIsFetchingPrices(false);
        }
      }
    };

    autoFetchPrices();
  }, [performance, selectedYear, availableYears, effectiveMissingPrices, loadCachedPrices, fetchCurrentPrices, fetchHistoricalPrices, calculatePerformance, displayHomeCurrency]);

  // 手動重新整理按鈕處理
  const handleRefreshPrices = async () => {
    if (!performance || !selectedYear || !availableYears) return;
    if (effectiveMissingPrices.length === 0) return;

    setIsFetchingPrices(true);
    setDismissMissingPricesOverlay(false); // 手動重新整理時再次顯示 overlay
    hasFetchedForYearRef.current = null; // 重設以允許重新抓取
    fetchRetryCountRef.current = 0; // 手動重新整理時重設重試計數

    try {
      const isCurrentYear = selectedYear === availableYears.currentYear;

      if (isCurrentYear) {
        // YTD：使用 Sina/Euronext 即時 API
        const fetchedPrices = await fetchCurrentPrices(
          effectiveMissingPrices,
          displayHomeCurrency
        );
        if (Object.keys(fetchedPrices).length > 0) {
          calculatePerformance(selectedYear, fetchedPrices);
        }
      } else {
        // 歷史年度：使用 Stooq 歷史 API
        const { yearStartPrices, yearEndPrices } = await fetchHistoricalPrices(
          effectiveMissingPrices,
          selectedYear,
          displayHomeCurrency
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
    autoFetchPricesRequestIdRef.current += 1;
    setSelectedYear(year);
    setPriceFetchFailed(false);
    setDismissMissingPricesOverlay(false); // 新年度重設 overlay dismiss 狀態
    hasFetchedForYearRef.current = null; // 重設以允許新年度重新抓取
    fetchRetryCountRef.current = 0; // 新年度重設重試計數
  };

  const handleMissingPricesSubmit = ({
    yearStartPrices,
    yearEndPrices,
  }: MissingPriceSubmissionPayload) => {
    if (selectedYear) {
      const hasYearEndPrices = Object.keys(yearEndPrices).length > 0;
      const hasYearStartPrices = Object.keys(yearStartPrices).length > 0;

      if (hasYearEndPrices || hasYearStartPrices) {
        calculatePerformance(selectedYear, yearEndPrices, yearStartPrices);
      }
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

  const isSelectedYearPerformance = Boolean(
    performance &&
    selectedYear != null &&
    performance.year === selectedYear,
  );

  const selectedYearPerformanceValues = useMemo(() => {
    if (!isSelectedYearPerformance || !performance) {
      return {
        selectedCurrencyLabel: null as string | null,
        modifiedDietzValue: null as number | null,
        timeWeightedReturnValue: null as number | null,
        totalReturnValue: null as number | null,
        startValue: null as number | null,
        endValue: null as number | null,
        netContributionValue: null as number | null,
      };
    }

    const selectedCurrencyLabelValue = effectiveCurrencyMode === 'source'
      ? performance.sourceCurrency
      : displayHomeCurrency;

    const modifiedDietzValueForYear = effectiveCurrencyMode === 'source'
      ? performance.modifiedDietzPercentageSource
      : performance.modifiedDietzPercentage;

    const timeWeightedReturnValueForYear = effectiveCurrencyMode === 'source'
      ? performance.timeWeightedReturnPercentageSource
      : performance.timeWeightedReturnPercentage;

    const totalReturnValueForYear = effectiveCurrencyMode === 'source'
      ? performance.totalReturnPercentageSource
      : performance.totalReturnPercentage;

    const startValueForYear = effectiveCurrencyMode === 'source'
      ? performance.startValueSource
      : performance.startValueHome;

    const endValueForYear = effectiveCurrencyMode === 'source'
      ? performance.endValueSource
      : performance.endValueHome;

    const netContributionValueForYear = effectiveCurrencyMode === 'source'
      ? performance.netContributionsSource
      : performance.netContributionsHome;

    return {
      selectedCurrencyLabel: selectedCurrencyLabelValue,
      modifiedDietzValue: modifiedDietzValueForYear,
      timeWeightedReturnValue: timeWeightedReturnValueForYear,
      totalReturnValue: totalReturnValueForYear,
      startValue: startValueForYear,
      endValue: endValueForYear,
      netContributionValue: netContributionValueForYear,
    };
  }, [isSelectedYearPerformance, performance, effectiveCurrencyMode, displayHomeCurrency]);

  const {
    selectedCurrencyLabel,
    modifiedDietzValue,
    timeWeightedReturnValue,
    totalReturnValue,
    startValue,
    endValue,
    netContributionValue,
  } = selectedYearPerformanceValues;

  const coverageInfoText = useMemo(() => {
    if (!isSelectedYearPerformance || !performance) return null;

    if (performance.coverageStartDate && performance.coverageDays != null) {
      return `覆蓋起始：${performance.coverageStartDate}（${performance.coverageDays} 天）`;
    }

    if (performance.coverageStartDate) {
      return `覆蓋起始：${performance.coverageStartDate}`;
    }

    if (performance.coverageDays != null) {
      return `覆蓋天數：${performance.coverageDays} 天`;
    }

    return null;
  }, [isSelectedYearPerformance, performance]);

  const reliabilitySignals = useMemo(() => {
    if (!isSelectedYearPerformance || !performance) return [] as string[];

    const signals: string[] = [];

    if (performance.coverageDays == null || performance.coverageDays < MINIMUM_RELIABLE_COVERAGE_DAYS) {
      signals.push('資料覆蓋有限');
    }

    if (performance.usesPartialHistoryAssumption === true) {
      signals.push('此年度含節錄匯入假設');
    }

    if (performance.hasOpeningBaseline === true) {
      signals.push('已套用期初基準');
    }

    return signals;
  }, [isSelectedYearPerformance, performance]);

  const returnDisplayDegradeHint = useMemo(() => {
    if (!isSelectedYearPerformance || !performance || !performance.shouldDegradeReturnDisplay) {
      return null;
    }

    const reasonCode = performance.returnDisplayDegradeReasonCode?.trim() ?? '';
    if (reasonCode && RETURN_DISPLAY_DEGRADE_REASON_COPY[reasonCode]) {
      return RETURN_DISPLAY_DEGRADE_REASON_COPY[reasonCode];
    }

    const backendMessage = performance.returnDisplayDegradeReasonMessage?.trim();
    if (backendMessage) {
      return `此年度資金加權報酬率（MD）信度偏低（${backendMessage}）`;
    }

    return '此年度資金加權報酬率（MD）信度偏低，請優先參考年度摘要與資料覆蓋訊號。';
  }, [isSelectedYearPerformance, performance]);

  const isPerformanceValueReady = Boolean(
    performance &&
    selectedYear != null &&
    performance.year === selectedYear &&
    performance.isComplete &&
    selectedCurrencyLabel != null &&
    modifiedDietzValue != null &&
    timeWeightedReturnValue != null &&
    totalReturnValue != null &&
    startValue != null &&
    endValue != null &&
    netContributionValue != null
  );

  const performanceLoadingStageFeedback = useMemo(
    () => getPerformanceLoadingStageFeedback({
      elapsedMs: performanceLoadingElapsedMs,
      selectedYear,
      currentYear: availableYears?.currentYear ?? null,
      hasMissingPrices: effectiveMissingPrices.length > 0,
      isFetchingPrices,
    }),
    [
      performanceLoadingElapsedMs,
      selectedYear,
      availableYears?.currentYear,
      effectiveMissingPrices.length,
      isFetchingPrices,
    ],
  );

  const selectedCustomBenchmarkIds = useMemo(
    () => customBenchmarks
      .filter(b => selectedBenchmarks.includes(`custom_${b.id}`))
      .map(b => b.id),
    [customBenchmarks, selectedBenchmarks]
  );

  const allDataReady = useMemo(() => {
    if (!selectedYear) return false;

    const systemBenchmarks = selectedBenchmarks.filter(k => !k.startsWith('custom_'));
    const allSystemBenchmarksReady = systemBenchmarks.length === 0 ||
      systemBenchmarks.every(k => (
        benchmarkReturns[k] !== undefined &&
        benchmarkReturnYears[k] === selectedYear
      ));

    const allCustomBenchmarksReady = selectedCustomBenchmarkIds.length === 0 ||
      selectedCustomBenchmarkIds.every(id => (
        customBenchmarkReturns[id] !== undefined &&
        customBenchmarkReturnYears[id] === selectedYear
      ));

    return allSystemBenchmarksReady && allCustomBenchmarksReady;
  }, [
    selectedYear,
    selectedBenchmarks,
    benchmarkReturns,
    benchmarkReturnYears,
    selectedCustomBenchmarkIds,
    customBenchmarkReturns,
    customBenchmarkReturnYears,
  ]);

  const benchmarkChartData = useMemo(() => {
    if (!performance) return [];

    const portfolioReturn = effectiveCurrencyMode === 'home'
      ? performance.modifiedDietzPercentage
      : performance.modifiedDietzPercentageSource;

    return [
      // Only include portfolio return if value exists
      ...(portfolioReturn != null
        ? [{
            label: `我的投資組合 (${effectiveCurrencyMode === 'home' ? displayHomeCurrency : performance.sourceCurrency})`,
            value: portfolioReturn,
            tooltip: `${selectedYear} 年度報酬率（資金加權報酬率 / Modified Dietz）`,
          }]
        : []),
      /* FR-134: 過濾掉資料為 null 的基準，不顯示為 0 */
      ...selectedBenchmarks
        .filter(benchmarkKey => (
          !benchmarkKey.startsWith('custom_') &&
          benchmarkReturnYears[benchmarkKey] === selectedYear &&
          benchmarkReturns[benchmarkKey] != null
        ))
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
      /* 自訂使用者基準 - 僅顯示已選擇且屬於當前年度 */
      ...customBenchmarks
        .filter(b => {
          const customKey = `custom_${b.id}`;
          return (
            selectedBenchmarks.includes(customKey) &&
            customBenchmarkReturnYears[b.id] === selectedYear &&
            customBenchmarkReturns[b.id] != null
          );
        })
        .map(b => ({
          label: b.displayName || b.ticker,
          value: customBenchmarkReturns[b.id]!,
          tooltip: `${selectedYear} 年度報酬率（自訂）`,
        })),
    ];
  }, [
    performance,
    effectiveCurrencyMode,
    displayHomeCurrency,
    selectedYear,
    selectedBenchmarks,
    benchmarkReturns,
    benchmarkReturnYears,
    benchmarkReturnSources,
    customBenchmarks,
    customBenchmarkReturns,
    customBenchmarkReturnYears,
  ]);

  // 固定以「選擇中的 benchmark 數量」保留圖表高度，避免資料分批完成時高度跳動
  const benchmarkChartHeight = useMemo(() => {
    const systemBenchmarkCount = selectedBenchmarks.filter(k => !k.startsWith('custom_')).length;
    return 80 + (systemBenchmarkCount + selectedCustomBenchmarkIds.length + 1) * 40;
  }, [selectedBenchmarks, selectedCustomBenchmarkIds.length]);

  const hiddenBenchmarkLabels = useMemo(() => {
    if (!selectedYear) return [];

    const hiddenSystemBenchmarks = selectedBenchmarks
      .filter(k => (
        !k.startsWith('custom_') &&
        benchmarkReturnYears[k] === selectedYear &&
        benchmarkReturns[k] == null
      ))
      .map(k => BENCHMARK_OPTIONS.find(b => b.key === k)?.label ?? k);

    const hiddenCustomBenchmarks = customBenchmarks
      .filter(b => (
        selectedBenchmarks.includes(`custom_${b.id}`) &&
        customBenchmarkReturnYears[b.id] === selectedYear &&
        customBenchmarkReturns[b.id] == null
      ))
      .map(b => b.displayName || b.ticker);

    return [...hiddenSystemBenchmarks, ...hiddenCustomBenchmarks];
  }, [
    selectedYear,
    selectedBenchmarks,
    benchmarkReturns,
    benchmarkReturnYears,
    customBenchmarks,
    customBenchmarkReturns,
    customBenchmarkReturnYears,
  ]);

  // 注意：isLoadingPortfolio 和 !portfolio 檢查現在在父元件 PerformancePage 中

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* 頁首 */}
        <div className="mb-6 space-y-4">
          <div>
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">歷史績效</h1>
            <p className="text-[var(--text-secondary)] text-sm mt-1">
              查看投資組合的年度績效表現
            </p>
          </div>

          <div className="flex flex-wrap items-start gap-3" data-testid="performance-control-row">
            <PortfolioSelector onCreateNew={onCreatePortfolio} />

            <div className="flex w-full flex-wrap items-center justify-start gap-3 sm:w-auto sm:ml-auto sm:justify-end">
              {performance && (
                <CurrencyToggle
                  value={effectiveCurrencyMode}
                  onChange={handleCurrencyModeChange}
                  sourceCurrency={performance.sourceCurrency}
                  homeCurrency={displayHomeCurrency}
                  allowSourceMode={!isAggregate}
                />
              )}
              <YearSelector
                years={availableYears?.years ?? []}
                selectedYear={selectedYear}
                currentYear={availableYears?.currentYear ?? new Date().getFullYear()}
                onChange={handleYearChange}
                isLoading={isLoadingYears}
              />
            </div>
          </div>
        </div>

        {error && (
          <div className="card-dark p-4 mb-6 border-l-4 border-[var(--color-danger)]">
            <p className="text-[var(--color-danger)]">{error}</p>
          </div>
        )}

        {/* 績效卡片 */}
        {isLoadingPerformance && !performance ? (
          <div className="card-dark p-8" role="status" aria-live="polite">
            <div className="flex items-center justify-center gap-2">
              <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
              <span className="text-[var(--text-muted)]">計算績效中...</span>
            </div>
            <div className="mt-4 rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/60 p-4">
              <p className="text-xs text-[var(--text-muted)]">{PERFORMANCE_LOADING_STAGE_FLOW_COPY}</p>
              <p className="mt-2 text-sm text-[var(--text-primary)]">
                目前階段：{performanceLoadingStageFeedback.currentStage}
              </p>
              <p className="mt-1 text-xs text-[var(--text-secondary)]">
                {performanceLoadingStageFeedback.hint}
              </p>
            </div>
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
            {effectiveMissingPrices.length > 0 && !dismissMissingPricesOverlay && priceFetchFailed && (
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
                      const uniqueTickers = effectiveMissingPrices
                        .map(mp => mp.ticker)
                        .filter((ticker, index, arr) => arr.indexOf(ticker) === index);
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

            {/* 績效指標 - 年度報酬率卡片 */}
            <div className="grid grid-cols-1 gap-6 mb-6">
              <div className={`card-dark p-6 ${effectiveCurrencyMode === 'source' ? 'border-l-4 border-[var(--accent-peach)]' : ''}`}>
                <div className="flex items-center gap-2 mb-4">
                  <Calendar className="w-5 h-5 text-[var(--accent-peach)]" />
                  <h3 className="text-[var(--text-muted)]">
                    {selectedYear} 年度報酬 {selectedCurrencyLabel ? `(${selectedCurrencyLabel})` : ''}
                  </h3>
                  <div className="relative group">
                    <button
                      type="button"
                      aria-label="年度報酬說明"
                      aria-describedby="annual-return-tooltip"
                      className="inline-flex items-center rounded-sm text-[var(--text-muted)] cursor-help"
                    >
                      <Info className="w-4 h-4" aria-hidden="true" />
                    </button>
                    <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block group-focus-within:block z-10">
                      <div
                        id="annual-return-tooltip"
                        role="tooltip"
                        className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap"
                      >
                        {effectiveCurrencyMode === 'source'
                          ? '原幣報酬率（不含匯率變動）'
                          : `${performance.transactionCount} 筆交易`}
                      </div>
                    </div>
                  </div>
                </div>

                {!isPerformanceValueReady ? (
                  <div className="py-2" role="status" aria-live="polite">
                    <div className="flex items-center gap-2">
                      <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                      <span className="text-lg text-[var(--text-muted)]">計算績效中...</span>
                    </div>
                    <p className="mt-2 text-xs text-[var(--text-secondary)]">
                      目前階段：{performanceLoadingStageFeedback.currentStage}。
                      {performanceLoadingStageFeedback.hint}
                    </p>
                  </div>
                ) : (
                  <>
                    {returnDisplayDegradeHint && (
                      <div className="mb-4 rounded-lg border border-[var(--color-warning)]/40 bg-[var(--color-warning)]/10 p-3">
                        <p className="text-sm text-[var(--color-warning)]">
                          低信度年度：{returnDisplayDegradeHint}
                        </p>
                      </div>
                    )}
                    {performance.hasRecentLargeInflowWarning && (
                      <div className="mb-4 rounded-lg border border-[var(--color-warning)]/40 bg-[var(--color-warning)]/10 p-3">
                        <p className="text-sm text-[var(--color-warning)]">
                          近期大額資金異動可能導致資金加權報酬率短期波動。
                        </p>
                      </div>
                    )}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      {/* 資金加權報酬率 */}
                      <div>
                        <div className="flex items-center gap-1 mb-1">
                          <p className="text-sm text-[var(--text-muted)]">資金加權報酬率</p>
                          <div className="relative group">
                            <button
                              type="button"
                              aria-label="資金加權報酬率說明"
                              aria-describedby="md-tooltip"
                              className="inline-flex items-center rounded-sm text-[var(--text-muted)] cursor-help"
                            >
                              <Info className="w-4 h-4" aria-hidden="true" />
                            </button>
                            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block group-focus-within:block z-10">
                              <div
                                id="md-tooltip"
                                role="tooltip"
                                className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap"
                              >
                                衡量比例的重壓 (Modified Dietz)
                              </div>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          {modifiedDietzValue! >= 0 ? (
                            <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                          ) : (
                            <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                          )}
                          <span className={`text-3xl font-bold number-display ${
                            modifiedDietzValue! >= 0 ? 'number-positive' : 'number-negative'
                          }`}>
                            {formatPercent(modifiedDietzValue)}
                          </span>
                        </div>
                      </div>

                      {/* 時間加權報酬率 */}
                      <div>
                        <div className="flex items-center gap-1 mb-1">
                          <p className="text-sm text-[var(--text-muted)]">時間加權報酬率</p>
                          <div className="relative group">
                            <button
                              type="button"
                              aria-label="時間加權報酬率說明"
                              aria-describedby="twr-tooltip"
                              className="inline-flex items-center rounded-sm text-[var(--text-muted)] cursor-help"
                            >
                              <Info className="w-4 h-4" aria-hidden="true" />
                            </button>
                            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block group-focus-within:block z-10">
                              <div
                                id="twr-tooltip"
                                role="tooltip"
                                className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap"
                              >
                                衡量本金的重壓 (TWR)
                              </div>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          {timeWeightedReturnValue! >= 0 ? (
                            <TrendingUp className="w-6 h-6 text-[var(--color-success)]" />
                          ) : (
                            <TrendingDown className="w-6 h-6 text-[var(--color-danger)]" />
                          )}
                          <span className={`text-3xl font-bold number-display ${
                            timeWeightedReturnValue! >= 0 ? 'number-positive' : 'number-negative'
                          }`}>
                            {formatPercent(timeWeightedReturnValue)}
                          </span>
                        </div>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>

            {/* Value Summary */}
            <div className={`card-dark p-6 mb-6 ${effectiveCurrencyMode === 'source' ? 'border-l-4 border-[var(--accent-peach)]' : ''}`}>
              <h3 className="text-lg font-bold text-[var(--text-primary)] mb-4">
                {selectedYear} 年度摘要 {selectedCurrencyLabel ? `(${selectedCurrencyLabel})` : ''}
              </h3>
              {(coverageInfoText || reliabilitySignals.length > 0) && (
                <div className="mb-4 rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/50 p-3">
                  {coverageInfoText && (
                    <p className="text-xs text-[var(--text-muted)]">{coverageInfoText}</p>
                  )}
                  {reliabilitySignals.length > 0 && (
                    <ul className="mt-2 space-y-1">
                      {reliabilitySignals.map((signal) => (
                        <li key={signal} className="text-sm text-[var(--text-secondary)]">
                          • {signal}
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
              {!isPerformanceValueReady ? (
                <div className="py-2" role="status" aria-live="polite">
                  <div className="flex items-center gap-2">
                    <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                    <span className="text-lg text-[var(--text-muted)]">計算績效中...</span>
                  </div>
                  <p className="mt-2 text-xs text-[var(--text-secondary)]">
                    目前階段：{performanceLoadingStageFeedback.currentStage}。
                    {performanceLoadingStageFeedback.hint}
                  </p>
                </div>
              ) : (
                <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">年初價值</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {startValue === 0
                        ? '首年'
                        : `${formatCurrency(startValue)} ${selectedCurrencyLabel}`}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">
                      {selectedYear === availableYears?.currentYear ? '目前價值' : '年底價值'}
                    </p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {formatCurrency(endValue)} {selectedCurrencyLabel}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">淨投入</p>
                    <p className="text-lg font-medium text-[var(--text-primary)] number-display">
                      {formatCurrency(netContributionValue)} {selectedCurrencyLabel}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">總報酬率</p>
                    <p className={`text-lg font-medium number-display ${(totalReturnValue ?? 0) >= 0 ? 'number-positive' : 'number-negative'}`}>
                      {formatPercent(totalReturnValue)}
                    </p>
                  </div>
                  <div>
                    <p className="text-sm text-[var(--text-muted)]">淨獲利</p>
                    {(() => {
                      const profit = endValue! - startValue! - netContributionValue!;

                      return (
                        <p className={`text-lg font-medium number-display ${profit >= 0 ? 'number-positive' : 'number-negative'}`}>
                          {formatCurrency(profit)} {selectedCurrencyLabel}
                        </p>
                      );
                    })()}
                  </div>
                </div>
              )}
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
                {!allDataReady ? (
                  <div className="flex items-center justify-center" style={{ minHeight: benchmarkChartHeight }}>
                    <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
                    <span className="ml-2 text-[var(--text-muted)]">載入基準報酬中...</span>
                  </div>
                ) : (
                  <>
                    <PerformanceBarChart
                      data={benchmarkChartData}
                      height={benchmarkChartHeight}
                    />
                    {/* FR-134: 顯示因資料不可用而隱藏的基準 */}
                    {hiddenBenchmarkLabels.length > 0 && (
                      <p className="text-xs text-[var(--color-warning)] mt-2">
                        以下指數因資料不可用已隱藏：
                        {hiddenBenchmarkLabels.join('、')}
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

        {/* 缺漏價格 Modal */}
        {performance && (
          <MissingPriceModal
            isOpen={showMissingPriceModal}
            onClose={() => setShowMissingPriceModal(false)}
            missingPrices={effectiveMissingPrices}
            year={selectedYear ?? new Date().getFullYear()}
            onSubmit={handleMissingPricesSubmit}
          />
        )}
      </div>
    </div>
  );
}

export default PerformancePage;
