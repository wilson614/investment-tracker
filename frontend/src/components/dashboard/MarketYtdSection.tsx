/**
 * MarketYtdSection
 *
 * 年初至今報酬（YTD）卡片：顯示多個 benchmark 的 YTD 報酬率，並提供排序與「最多 10 個」的選擇設定。
 *
 * 設定會寫入 localStorage（與 Performance 頁面的 benchmark 選擇相互呼應）。
 * 也支援用戶自訂基準（從 UserBenchmark API 載入）。
 */

import { useState, useMemo, useEffect } from 'react';
import { Loader2, Info, Settings, X, Check } from 'lucide-react';
import { useMarketYtdData } from '../../hooks/useMarketYtdData';
import { userBenchmarkApi, stockPriceApi, marketDataApi } from '../../services/api';
import type { MarketYtdReturn, UserBenchmark } from '../../types';
import { getEuronextSymbol } from '../../constants';

type YtdSortKey = 'ytd-desc' | 'ytd-asc' | 'name-asc' | 'name-desc';

const YTD_SORT_OPTIONS: { value: YtdSortKey; label: string }[] = [
  { value: 'ytd-desc', label: 'YTD ↓' },
  { value: 'ytd-asc', label: 'YTD ↑' },
  { value: 'name-asc', label: '名稱 A-Z' },
  { value: 'name-desc', label: '名稱 Z-A' },
];

const YTD_PREFS_KEY = 'ytd_benchmark_preferences';

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
  'Taiwan 0050': '台灣 0050',
};

/**
 * 系統內建 benchmark 列表（完整 11 個，與後端 MarketYtdService.Benchmarks 一致）。
 * 用於確保 availableBenchmarks 一定包含所有系統 benchmark（即使 API 暫時抓取失敗）。
 */
const SYSTEM_BENCHMARKS = [
  'All Country', 'US Large', 'US Small', 'Developed Markets Large', 'Developed Markets Small',
  'Dev ex US Large', 'Emerging Markets', 'Europe', 'Japan', 'China', 'Taiwan 0050'
];

// Default selected benchmarks (using English keys to match API) - 預設選中 10 個
const DEFAULT_BENCHMARKS = [
  'All Country', 'US Large', 'Developed Markets Large', 'Developed Markets Small',
  'Dev ex US Large', 'Emerging Markets', 'Europe', 'Japan', 'China', 'Taiwan 0050'
];

/**
 * 從 localStorage 讀取使用者選擇的 benchmarks。
 *
 * 回傳英文 key（例如 `All Country`），以符合 API 回傳與其他頁面的同步。
 */
function getSelectedBenchmarks(): string[] {
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
  return DEFAULT_BENCHMARKS;
}

/**
 * 將 benchmarks 寫入 localStorage（供 Dashboard/Performance 共用）。
 */
function saveSelectedBenchmarks(benchmarks: string[]): void {
  try {
    localStorage.setItem(YTD_PREFS_KEY, JSON.stringify(benchmarks));
  } catch {
    // Ignore
  }
}

interface YtdCardProps {
  item: MarketYtdReturn & { isCustom?: boolean };
}

function YtdCard({ item }: YtdCardProps) {
  const hasYtd = item.ytdReturnPercent != null;
  const isPositive = hasYtd && item.ytdReturnPercent! >= 0;
  // For custom benchmarks, use the symbol as the label
  const displayLabel = item.isCustom
    ? item.symbol
    : (BENCHMARK_LABELS[item.marketKey] || item.marketKey);

  return (
    <div className={`bg-[var(--bg-tertiary)] rounded-lg px-2 py-4 text-center ${item.isCustom ? 'border border-[var(--accent-peach)]/30' : ''}`}>
      <div className="text-sm text-[var(--text-primary)] truncate mb-1" title={displayLabel}>
        {displayLabel}
        {item.isCustom && <span className="ml-1 text-[10px] text-[var(--accent-peach)]">自訂</span>}
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
      <div className="font-mono text-xs text-[var(--text-muted)]">{item.symbol}</div>
    </div>
  );
}

interface MarketYtdSectionProps {
  className?: string;
}

export function MarketYtdSection({ className = '' }: MarketYtdSectionProps) {
  const { data, isLoading } = useMarketYtdData();
  const [sortKey, setSortKey] = useState<YtdSortKey>('ytd-desc');
  const [selectedBenchmarks, setSelectedBenchmarksState] = useState<string[]>(getSelectedBenchmarks);
  const [showSettings, setShowSettings] = useState(false);
  const [tempSelected, setTempSelected] = useState<string[]>(selectedBenchmarks);

  // Custom user benchmarks
  const [customBenchmarks, setCustomBenchmarks] = useState<UserBenchmark[]>([]);
  const [customBenchmarkReturns, setCustomBenchmarkReturns] = useState<Record<string, number | null>>({});
  const [isLoadingCustom, setIsLoadingCustom] = useState(false);

  // Load user's custom benchmarks
  useEffect(() => {
    const loadCustomBenchmarks = async () => {
      try {
        const benchmarks = await userBenchmarkApi.getAll();
        setCustomBenchmarks(benchmarks);
      } catch (err) {
        console.error('Failed to load custom benchmarks:', err);
      }
    };
    loadCustomBenchmarks();
  }, []);

  // Calculate YTD returns for custom benchmarks
  useEffect(() => {
    const calculateCustomReturns = async () => {
      if (customBenchmarks.length === 0) {
        setCustomBenchmarkReturns({});
        return;
      }

      setIsLoadingCustom(true);
      const currentYear = new Date().getFullYear();
      const yearStartDate = `${currentYear - 1}-12-31`;
      const newReturns: Record<string, number | null> = {};

      await Promise.all(
        customBenchmarks.map(async (benchmark) => {
          try {
            // Get year-start price
            const startPriceData = await marketDataApi.getHistoricalPrices(
              [benchmark.ticker],
              yearStartDate
            );
            const startPrice = startPriceData[benchmark.ticker]?.price;

            if (!startPrice) {
              newReturns[benchmark.id] = null;
              return;
            }

            // Get current price
            let endPrice: number | undefined;
            try {
              const quote = await stockPriceApi.getQuote(benchmark.market, benchmark.ticker);
              endPrice = quote?.price;
            } catch {
              // Try Euronext for UK ETFs
              const euronextInfo = getEuronextSymbol(benchmark.ticker);
              if (euronextInfo) {
                const euronextQuote = await marketDataApi.getEuronextQuote(
                  euronextInfo.isin,
                  euronextInfo.mic,
                  'TWD'
                );
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
      setIsLoadingCustom(false);
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
    let filtered = data.benchmarks.filter(b => selectedBenchmarks.includes(b.marketKey));

    // Sort
    return [...filtered].sort((a, b) => {
      switch (sortKey) {
        case 'ytd-asc':
          return (a.ytdReturnPercent ?? -Infinity) - (b.ytdReturnPercent ?? -Infinity);
        case 'ytd-desc':
          return (b.ytdReturnPercent ?? -Infinity) - (a.ytdReturnPercent ?? -Infinity);
        case 'name-asc':
          return a.marketKey.localeCompare(b.marketKey);
        case 'name-desc':
          return b.marketKey.localeCompare(a.marketKey);
        default:
          return 0;
      }
    });
  }, [data?.benchmarks, selectedBenchmarks, sortKey]);

  // Combine system benchmarks with custom benchmarks for display
  const allDisplayItems = useMemo(() => {
    const items: MarketYtdReturn[] = [];

    // Add system benchmarks
    if (filteredAndSorted) {
      items.push(...filteredAndSorted);
    }

    // Add custom benchmarks with calculated returns (only if selected)
    customBenchmarks.forEach(b => {
      const customKey = `custom_${b.id}`;
      // 只顯示被選中的自訂基準
      if (!selectedBenchmarks.includes(customKey)) return;

      const returnValue = customBenchmarkReturns[b.id];
      items.push({
        marketKey: customKey,
        symbol: b.ticker,
        ytdReturnPercent: returnValue ?? undefined,
        isCustom: true,
      } as MarketYtdReturn & { isCustom?: boolean });
    });

    // Sort the combined list
    return items.sort((a, b) => {
      switch (sortKey) {
        case 'ytd-asc':
          return (a.ytdReturnPercent ?? -Infinity) - (b.ytdReturnPercent ?? -Infinity);
        case 'ytd-desc':
          return (b.ytdReturnPercent ?? -Infinity) - (a.ytdReturnPercent ?? -Infinity);
        case 'name-asc':
          return a.marketKey.localeCompare(b.marketKey);
        case 'name-desc':
          return b.marketKey.localeCompare(a.marketKey);
        default:
          return 0;
      }
    });
  }, [filteredAndSorted, customBenchmarks, customBenchmarkReturns, sortKey, selectedBenchmarks]);

  const handleOpenSettings = () => {
    setTempSelected(selectedBenchmarks);
    setShowSettings(true);
  };

  const handleSaveSettings = () => {
    if (tempSelected.length > 0) {
      setSelectedBenchmarksState(tempSelected);
      saveSelectedBenchmarks(tempSelected);
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
                報酬率 = (現價 - 年初基準價) / 年初基準價 × 100
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
          <div className="flex items-center justify-center py-8">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--text-muted)]" />
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
