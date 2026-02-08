/**
 * MarketYtdSection
 *
 * 年初至今報酬 (YTD) 卡片：顯示多個 benchmark 的 YTD 報酬率，並提供排序與「最多 10 個」的選擇設定。
 *
 * 設定會寫入 localStorage （與 Performance 頁面的 benchmark 選擇相互呼應）。
 * 也支援用戶自訂基準（從 UserBenchmark API 載入）。
 */

import { useState, useMemo, useEffect, useCallback, useRef } from 'react';
import { Info, Settings, X, Check } from 'lucide-react';
import { DEFAULT_BENCHMARKS } from '../../constants';
import { useMarketYtdData } from '../../hooks/useMarketYtdData';
import { userBenchmarkApi, stockPriceApi, marketDataApi, userPreferencesApi } from '../../services/api';
import { Skeleton } from '../common/SkeletonLoader';
import type { MarketYtdReturn, UserBenchmark } from '../../types';
import { StockMarket } from '../../types';

type YtdSortKey = 'ytd-desc' | 'ytd-asc' | 'name-asc' | 'name-desc';

const YTD_SORT_OPTIONS: { value: YtdSortKey; label: string }[] = [
  { value: 'ytd-desc', label: 'YTD ↓' },
  { value: 'ytd-asc', label: 'YTD ↑' },
  { value: 'name-asc', label: '名稱 A-Z' },
  { value: 'name-desc', label: '名稱 Z-A' },
];

const YTD_PREFS_KEY = 'ytd_benchmark_preferences';
const CUSTOM_YTD_CACHE_KEY = 'custom_benchmark_ytd_cache';

/**
 * 英文 key → 中文顯示名稱（需與 backend `MarketYtdService.Benchmarks` 一致）。
 * 共 11 個系統內建 benchmark。
 */
const BENCHMARK_LABELS: Record<string, string> = {
  'All Country': '全球',
  'US Large': '美國大型',
  'US Small': '美國小型',
  'Developed Markets Large': '已開發大型',
  'Developed Markets Small': '已開發小型',
  'Dev ex US Large': '已開發非美',
  'Emerging Markets': '新興市場',
  'Europe': '歐洲',
  'Japan': '日本',
  'China': '中國',
  'Taiwan 0050': '台灣',
};

const STOCK_MARKET_LABELS: Record<number, string> = {
  [StockMarket.TW]: '台股',
  [StockMarket.US]: '美股',
  [StockMarket.UK]: '英股',
  [StockMarket.EU]: '歐股',
};

const STOCK_MARKET_VALUE_BY_NAME: Record<string, StockMarket> = {
  TW: StockMarket.TW,
  US: StockMarket.US,
  UK: StockMarket.UK,
  EU: StockMarket.EU,
};

function normalizeMarketValue(market: unknown): StockMarket {
  if (typeof market === 'number' && Number.isFinite(market)) {
    return market as StockMarket;
  }

  if (typeof market === 'string') {
    const normalized = market.trim().toUpperCase();
    if (/^\d+$/.test(normalized)) {
      return Number(normalized) as StockMarket;
    }

    return STOCK_MARKET_VALUE_BY_NAME[normalized] ?? StockMarket.US;
  }

  return StockMarket.US;
}

/**
 * 系統內建 benchmark 列表（完整 11 個，與後端 MarketYtdService.Benchmarks 一致）。
 * 用於確保 availableBenchmarks 一定包含所有系統 benchmark （即使 API 暫時抓取失敗）。
 */
const SYSTEM_BENCHMARKS = [
  'All Country', 'US Large', 'US Small', 'Developed Markets Large', 'Developed Markets Small',
  'Dev ex US Large', 'Emerging Markets', 'Europe', 'Japan', 'China', 'Taiwan 0050'
];

// Default selected benchmarks (using English keys to match API) - 預設選中 10 個
// Note: moved to shared constant in `src/constants`.

/**
 * 從 localStorage 讀取使用者選擇的 benchmarks （fallback 用）。
 *
 * 回傳英文 key （例如 `All Country`），以符合 API 回傳與其他頁面的同步。
 */
function getSelectedBenchmarksFromLocalStorage(): string[] {
  try {
    const stored = localStorage.getItem(YTD_PREFS_KEY);
    if (stored) {
      const benchmarks = JSON.parse(stored);
      if (Array.isArray(benchmarks) && benchmarks.length > 0) {
        return benchmarks;
      }
    }
  } catch {
    // Ignore
  }
  return [...DEFAULT_BENCHMARKS];
}

/**
 * 將 benchmarks 寫入 localStorage （fallback 用）。
 */
function saveSelectedBenchmarksToLocalStorage(benchmarks: string[]): void {
  try {
    localStorage.setItem(YTD_PREFS_KEY, JSON.stringify(benchmarks));
  } catch {
    // Ignore
  }
}

interface CustomBenchmarkCache {
  benchmarks: Array<{ id: string; ticker: string; market: number | string }>;
  returns: Record<string, number | null>;
}

/**
 * 從 localStorage 讀取自訂基準快取（包含清單和 YTD）。
 */
function loadCustomBenchmarkCache(): CustomBenchmarkCache {
  try {
    const cached = localStorage.getItem(CUSTOM_YTD_CACHE_KEY);
    if (cached) {
      const data = JSON.parse(cached);
      // 相容舊格式（只有 returns）
      if (data && typeof data === 'object' && !data.benchmarks) {
        return { benchmarks: [], returns: data };
      }
      return data;
    }
  } catch {
    // Ignore
  }
  return { benchmarks: [], returns: {} };
}

/**
 * 將自訂基準快取寫入 localStorage。
 */
function saveCustomBenchmarkCache(cache: CustomBenchmarkCache): void {
  try {
    localStorage.setItem(CUSTOM_YTD_CACHE_KEY, JSON.stringify(cache));
  } catch {
    // Ignore
  }
}

/**
 * YTD 卡片骨架：載入時的佔位元素。
 */
function YtdCardSkeleton() {
  return (
    <div className="bg-[var(--bg-tertiary)] rounded-lg px-2 py-4 text-center">
      <Skeleton width="w-16" height="h-4" className="mx-auto mb-2" />
      <Skeleton width="w-12" height="h-6" className="mx-auto mb-2" />
      <Skeleton width="w-10" height="h-3" className="mx-auto" />
    </div>
  );
}

type YtdCardItem = MarketYtdReturn & { isCustom?: boolean; market?: number };

interface YtdCardProps {
  item: YtdCardItem;
}

function YtdCard({ item }: YtdCardProps) {
  const hasYtd = item.ytdReturnPercent != null;
  const isPositive = hasYtd && item.ytdReturnPercent! >= 0;

  // 自訂基準：用 ticker
  // 系統基準：用中文對應名稱
  const displayLabel = item.isCustom
    ? item.symbol
    : (BENCHMARK_LABELS[item.marketKey] || item.marketKey);

  const customMarketLabel = item.isCustom
    ? (STOCK_MARKET_LABELS[item.market as number] ?? '市場')
    : null;

  return (
    <div className={`bg-[var(--bg-tertiary)] rounded-lg px-2 py-4 text-center ${item.isCustom ? 'border border-[var(--accent-peach)]/30' : ''}`}>
      <div className="text-sm text-[var(--text-primary)] truncate mb-1" title={displayLabel}>
        {displayLabel}
      </div>
      {item.error ? (
        <div className="text-base text-[var(--color-warning)] my-1">N/A</div>
      ) : hasYtd ? (
        <div className={`text-xl font-bold font-mono my-1 ${isPositive ? 'text-green-400' : 'text-red-400'}`}>
          {isPositive ? '+' : ''}{item.ytdReturnPercent!.toFixed(1)}%
        </div>
      ) : (
        <div className="text-base text-[var(--text-muted)] my-1">--</div>
      )}
      {/* 系統基準顯示 symbol，自訂基準顯示市場 */}
      {item.isCustom ? (
        <div className="text-xs text-[var(--accent-peach)]">{customMarketLabel}</div>
      ) : (
        <div className="font-mono text-xs text-[var(--text-muted)]">{item.symbol}</div>
      )}
    </div>
  );
}

interface MarketYtdSectionProps {
  className?: string;
}

// 在元件外部快取初始值（只讀一次 localStorage）
const initialCustomCache = loadCustomBenchmarkCache();
const initialHasCached =
  initialCustomCache.benchmarks.length > 0 && Object.keys(initialCustomCache.returns).length > 0;

export function MarketYtdSection({ className = '' }: MarketYtdSectionProps) {
  const { data, isLoading, isRefreshing } = useMarketYtdData();
  const [sortKey, setSortKey] = useState<YtdSortKey>('ytd-desc');
  const [selectedBenchmarks, setSelectedBenchmarksState] = useState<string[]>(getSelectedBenchmarksFromLocalStorage);
  const [showSettings, setShowSettings] = useState(false);
  const [tempSelected, setTempSelected] = useState<string[]>(selectedBenchmarks);

  // 背景更新時鎖定排序，避免頻繁跳動
  const [lockedOrder, setLockedOrder] = useState<string[] | null>(null);

  // Custom user benchmarks - 使用預先載入的快取
  const [customBenchmarks, setCustomBenchmarks] = useState<UserBenchmark[]>(
    initialCustomCache.benchmarks.map((b) => ({
      ...b,
      market: normalizeMarketValue(b.market),
      addedAt: '',
    } as UserBenchmark))
  );
  const [customBenchmarkReturns, setCustomBenchmarkReturns] = useState<Record<string, number | null>>(
    initialCustomCache.returns
  );
  const [isLoadingCustom, setIsLoadingCustom] = useState(!initialHasCached);
  const [customReturnsLoaded, setCustomReturnsLoaded] = useState(initialHasCached);
  const [isRefreshingCustom, setIsRefreshingCustom] = useState(false);

  // 追蹤是否已有快取（用於 useEffect 內部判斷）
  const hasCachedDataRef = useRef(initialHasCached);

  // Load user preferences from API
  useEffect(() => {
    const loadPreferences = async () => {
      try {
        const prefs = await userPreferencesApi.get();
        if (prefs.ytdBenchmarkPreferences) {
          const benchmarks = JSON.parse(prefs.ytdBenchmarkPreferences);
          if (Array.isArray(benchmarks) && benchmarks.length > 0) {
            setSelectedBenchmarksState(benchmarks);
            // Sync to localStorage for offline use
            saveSelectedBenchmarksToLocalStorage(benchmarks);
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
  const savePreferences = useCallback(async (benchmarks: string[]) => {
    // Always save to localStorage first (for immediate sync)
    saveSelectedBenchmarksToLocalStorage(benchmarks);

    // Then save to API
    try {
      await userPreferencesApi.update({
        ytdBenchmarkPreferences: JSON.stringify(benchmarks),
      });
    } catch (err) {
      console.error('Failed to save preferences to API:', err);
    }
  }, []);

  // Load user's custom benchmarks
  useEffect(() => {
    const loadCustomBenchmarks = async () => {
      const hasCached = hasCachedDataRef.current;

      // 若已有快取資料，保持畫面穩定，背景靜默更新
      if (!hasCached) {
        setIsLoadingCustom(true);
        setCustomReturnsLoaded(false);
      } else {
        setIsRefreshingCustom(true);
      }

      try {
        const benchmarks = await userBenchmarkApi.getAll();
        const normalizedBenchmarks = benchmarks.map((benchmark) => ({
          ...benchmark,
          market: normalizeMarketValue(benchmark.market),
        }));
        setCustomBenchmarks(normalizedBenchmarks);

        // 清理已刪除的自訂基準：從 selectedBenchmarks 中移除不存在的 custom_ key
        const validCustomKeys = new Set(normalizedBenchmarks.map(b => `custom_${b.id}`));
        setSelectedBenchmarksState(prev => {
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

        // 如果沒有自訂基準，直接標記載入完成
        if (normalizedBenchmarks.length === 0) {
          setIsLoadingCustom(false);
          setCustomReturnsLoaded(true);
        }
      } catch (err) {
        console.error('Failed to load custom benchmarks:', err);
        setIsLoadingCustom(false);
        setCustomReturnsLoaded(true);
      } finally {
        setIsRefreshingCustom(false);
      }
    };
    loadCustomBenchmarks();
  }, [savePreferences]);

  // Calculate YTD returns for custom benchmarks
  useEffect(() => {
    const calculateCustomReturns = async () => {
      if (customBenchmarks.length === 0) {
        setCustomBenchmarkReturns({});
        saveCustomBenchmarkCache({ benchmarks: [], returns: {} });
        return;
      }

      // 只在沒有快取資料時才顯示 loading （有快取時背景靜默更新）
      if (!hasCachedDataRef.current) {
        setIsLoadingCustom(true);
      }

      const currentYear = new Date().getFullYear();
      const yearStartDate = `${currentYear - 1}-12-31`;
      const newReturns: Record<string, number | null> = {};

      await Promise.all(
        customBenchmarks.map(async (benchmark) => {
          try {
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

            // Get current price
            let endPrice: number | undefined;
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
      // 儲存快取（包含清單和 YTD）
      saveCustomBenchmarkCache({
        benchmarks: customBenchmarks.map(b => ({ id: b.id, ticker: b.ticker, market: b.market })),
        returns: newReturns,
      });
      hasCachedDataRef.current = true; // 標記已有快取
      setIsLoadingCustom(false);
      setCustomReturnsLoaded(true);
    };

    calculateCustomReturns();
  }, [customBenchmarks]);

  const availableBenchmarks = useMemo(() => {
    // 使用靜態定義的 SYSTEM_BENCHMARKS 確保所有系統 benchmark 都可選
    // 這樣即使 API 暫時抓取失敗，也不會少掉任何選項
    return SYSTEM_BENCHMARKS;
  }, []);

  const filteredAndSorted = useMemo(() => {
    if (!data?.benchmarks) return null;

    // Filter by selected benchmarks
    const filtered = data.benchmarks.filter(b => selectedBenchmarks.includes(b.marketKey));

    // Sort
    return [...filtered].sort((a, b) => {
      switch (sortKey) {
        case 'ytd-asc':
          return (a.ytdReturnPercent ?? -Infinity) - (b.ytdReturnPercent ?? -Infinity);
        case 'ytd-desc':
          return (b.ytdReturnPercent ?? -Infinity) - (a.ytdReturnPercent ?? -Infinity);
        case 'name-asc':
          return (BENCHMARK_LABELS[a.marketKey] || a.marketKey).localeCompare(BENCHMARK_LABELS[b.marketKey] || b.marketKey);
        case 'name-desc':
          return (BENCHMARK_LABELS[b.marketKey] || b.marketKey).localeCompare(BENCHMARK_LABELS[a.marketKey] || a.marketKey);
        default:
          return 0;
      }
    }) as YtdCardItem[];
  }, [data?.benchmarks, selectedBenchmarks, sortKey]);

  // Combine system benchmarks with custom benchmarks for display
  const allDisplayItems = useMemo(() => {
    const items: YtdCardItem[] = [];

    // Add system benchmarks
    if (filteredAndSorted) {
      items.push(...filteredAndSorted);
    }

    // Add custom benchmarks with calculated returns (only if selected AND loaded)
    // 沒有快取時：等自訂基準的 YTD 計算完成後一次加入，避免空框與半拍
    // 有快取時：customReturnsLoaded 會是 true，直接顯示快取並背景更新
    if (customReturnsLoaded) {
      customBenchmarks.forEach(b => {
        const customKey = `custom_${b.id}`;
        // 只顯示被選中的自訂基準
        if (!selectedBenchmarks.includes(customKey)) return;

        const returnValue = customBenchmarkReturns[b.id];
        items.push({
          marketKey: customKey,
          symbol: b.ticker,
          name: b.ticker,
          jan1Price: null,
          currentPrice: null,
          ytdReturnPercent: returnValue ?? null,
          fetchedAt: null,
          error: null,
          isCustom: true,
          market: b.market,
        });
      });
    }

    // Sort the combined list
    const sorted = items.sort((a, b) => {
      const aIsCustom = a.isCustom === true;
      const bIsCustom = b.isCustom === true;

      switch (sortKey) {
        case 'ytd-asc':
          return (a.ytdReturnPercent ?? -Infinity) - (b.ytdReturnPercent ?? -Infinity);
        case 'ytd-desc':
          return (b.ytdReturnPercent ?? -Infinity) - (a.ytdReturnPercent ?? -Infinity);
        case 'name-asc': {
          const aLabel = aIsCustom ? a.symbol : (BENCHMARK_LABELS[a.marketKey] || a.marketKey);
          const bLabel = bIsCustom ? b.symbol : (BENCHMARK_LABELS[b.marketKey] || b.marketKey);
          return aLabel.localeCompare(bLabel);
        }
        case 'name-desc': {
          const aLabel = aIsCustom ? a.symbol : (BENCHMARK_LABELS[a.marketKey] || a.marketKey);
          const bLabel = bIsCustom ? b.symbol : (BENCHMARK_LABELS[b.marketKey] || b.marketKey);
          return bLabel.localeCompare(aLabel);
        }
        default:
          return 0;
      }
    });

    // 背景更新中：鎖定目前順序，避免跳動
    if (lockedOrder && (isRefreshing || isRefreshingCustom)) {
      const orderIndex = new Map(lockedOrder.map((key, index) => [key, index]));
      return [...sorted].sort((a, b) => {
        const aIndex = orderIndex.get(a.marketKey);
        const bIndex = orderIndex.get(b.marketKey);
        if (aIndex == null && bIndex == null) return 0;
        if (aIndex == null) return 1;
        if (bIndex == null) return -1;
        return aIndex - bIndex;
      });
    }

    return sorted;
  }, [filteredAndSorted, customBenchmarks, customBenchmarkReturns, sortKey, selectedBenchmarks, customReturnsLoaded, lockedOrder, isRefreshing, isRefreshingCustom]);

  const handleOpenSettings = () => {
    setTempSelected(selectedBenchmarks);
    setShowSettings(true);
  };

  // 背景更新開始時鎖定目前排序；結束後解除鎖定
  useEffect(() => {
    if ((isRefreshing || isRefreshingCustom) && !lockedOrder && allDisplayItems.length > 0) {
      setLockedOrder(allDisplayItems.map(i => i.marketKey));
      return;
    }

    if (!(isRefreshing || isRefreshingCustom) && lockedOrder) {
      setLockedOrder(null);
    }
  }, [isRefreshing, isRefreshingCustom, lockedOrder, allDisplayItems]);

  const handleSaveSettings = () => {
    if (tempSelected.length > 0) {
      setSelectedBenchmarksState(tempSelected);
      savePreferences(tempSelected);
    }
    setShowSettings(false);
  };

  const toggleBenchmark = (key: string) => {
    setTempSelected(prev => {
      if (prev.includes(key)) {
        if (prev.length === 1) return prev;
        return prev.filter(k => k !== key);
      }
      // Enforce max 10 benchmarks
      if (prev.length >= 10) return prev;
      return [...prev, key];
    });
  };

  const handleResetToDefault = () => {
    setTempSelected([...DEFAULT_BENCHMARKS]);
  };

  return (
    <div className={`card-dark ${className}`}>
      <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div>
            <h2 className="text-lg font-bold text-[var(--text-primary)]">年初至今報酬</h2>
            <p className="text-xs text-[var(--text-muted)]">
              {data?.year ? `${data.year} 年度績效 (YTD)` : '基準 ETF 年度績效'}
            </p>
          </div>
          <div className="relative group">
            <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
              <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                報酬率 = （現價 - 年初基準價） / 年初基準價 × 100
                （年初基準價採前一年 12 月收盤價）
              </div>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as YtdSortKey)}
            className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded px-2 py-1.5 text-xs text-[var(--text-secondary)] focus:outline-none focus:border-[var(--accent-peach)] h-8"
          >
            {YTD_SORT_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <button onClick={handleOpenSettings} className="btn-dark p-2 h-8 flex items-center justify-center" title="選擇基準">
            <Settings className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Settings Modal */}
      {showSettings && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="card-dark w-full max-w-md mx-4">
            <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
              <h3 className="text-lg font-bold text-[var(--text-primary)]">選擇顯示基準</h3>
              <button onClick={() => setShowSettings(false)} className="text-[var(--text-muted)] hover:text-[var(--text-primary)]">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="p-5 max-h-[50vh] overflow-y-auto">
              {tempSelected.length >= 10 && (
                <div className="mb-3 px-3 py-2 bg-[var(--accent-peach)]/10 border border-[var(--accent-peach)]/30 rounded-lg text-sm text-[var(--text-muted)]">
                  已達上限（最多 10 個）
                </div>
              )}
              {/* 系統內建基準 */}
              <div className="mb-4">
                <h4 className="text-xs text-[var(--text-muted)] mb-2">系統內建基準</h4>
                <div className="grid grid-cols-2 gap-2">
                  {availableBenchmarks.map((key) => {
                    const isSelected = tempSelected.includes(key);
                    const isAtLimit = tempSelected.length >= 10;
                    const isDisabled = !isSelected && isAtLimit;

                    return (
                    <button
                      key={key}
                      onClick={() => toggleBenchmark(key)}
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
                      <span className="text-sm truncate">{BENCHMARK_LABELS[key] || key}</span>
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
                      const isSelected = tempSelected.includes(customKey);
                      const isAtLimit = tempSelected.length >= 10;
                      const isDisabled = !isSelected && isAtLimit;

                      return (
                        <button
                          key={customKey}
                          onClick={() => toggleBenchmark(customKey)}
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
                          <span className="text-sm truncate">{b.ticker}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
            <div className="px-5 py-4 border-t border-[var(--border-color)] flex justify-between">
              <button onClick={handleResetToDefault} className="text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)]">
                重置為預設
              </button>
              <div className="flex gap-3">
                <button onClick={() => setShowSettings(false)} className="btn-dark px-4 py-2">取消</button>
                <button onClick={handleSaveSettings} className="btn-accent px-4 py-2">儲存</button>
              </div>
            </div>
          </div>
        </div>
      )}

      <div className="p-5">
        {(isLoading || isLoadingCustom) && !data ? (
          <div className="grid grid-cols-5 gap-2">
            {Array.from({ length: 10 }).map((_, i) => (
              <YtdCardSkeleton key={i} />
            ))}
          </div>
        ) : allDisplayItems.length > 0 ? (
          <div className="grid grid-cols-5 gap-2">
            {allDisplayItems.map((item) => (
              <YtdCard key={item.marketKey} item={item} />
            ))}
          </div>
        ) : (
          <div className="text-center py-8 text-[var(--text-muted)]">
            無可用資料
          </div>
        )}
      </div>
    </div>
  );
}
