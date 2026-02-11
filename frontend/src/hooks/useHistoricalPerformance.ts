import { useState, useEffect, useCallback, useRef } from 'react';
import { portfolioApi } from '../services/api';
import type { AvailableYears, YearPerformance, YearEndPriceInfo } from '../types';

interface UseHistoricalPerformanceOptions {
  portfolioId: string;
  isAggregate?: boolean;
  autoFetch?: boolean;
}

interface UseHistoricalPerformanceResult {
  availableYears: AvailableYears | null;
  selectedYear: number | null;
  performance: YearPerformance | null;
  isLoadingYears: boolean;
  isLoadingPerformance: boolean;
  error: string | null;
  setSelectedYear: (year: number) => void;
  calculatePerformance: (year: number, yearEndPrices?: Record<string, YearEndPriceInfo>, yearStartPrices?: Record<string, YearEndPriceInfo>) => Promise<void>;
  refresh: () => Promise<void>;
}

/**
 * 從 localStorage 載入指定命名空間的績效快取資料。
 * - 單一投資組合：namespace = portfolioId
 * - 彙總模式：namespace = aggregate
 */
function loadCacheForPortfolio(cacheNamespace: string): {
  availableYears: AvailableYears | null;
  selectedYear: number | null;
  performance: YearPerformance | null;
} {
  try {
    const cachedYears = localStorage.getItem(`perf_years_${cacheNamespace}`);
    if (cachedYears) {
      const years = JSON.parse(cachedYears) as AvailableYears;
      const year = years.currentYear ?? years.years?.[0] ?? null;
      if (year) {
        const cachedPerf = localStorage.getItem(`perf_data_${cacheNamespace}_${year}`);
        return {
          availableYears: years,
          selectedYear: year,
          performance: cachedPerf ? JSON.parse(cachedPerf) : null,
        };
      }
      return { availableYears: years, selectedYear: year, performance: null };
    }
  } catch { /* 忽略快取讀取錯誤 */ }
  return { availableYears: null, selectedYear: null, performance: null };
}

export function useHistoricalPerformance({
  portfolioId,
  isAggregate = false,
  autoFetch = true,
}: UseHistoricalPerformanceOptions): UseHistoricalPerformanceResult {
  const cacheNamespace = isAggregate ? 'aggregate' : portfolioId;

  // 元件掛載時一次性載入所有快取資料，確保同步執行以避免閃爍
  const [initialCache] = useState(() => loadCacheForPortfolio(cacheNamespace));

  // 使用快取資料初始化狀態，實現即時顯示
  const [availableYears, setAvailableYears] = useState<AvailableYears | null>(initialCache.availableYears);
  const [selectedYear, setSelectedYear] = useState<number | null>(initialCache.selectedYear);
  const [performance, setPerformance] = useState<YearPerformance | null>(initialCache.performance);
  const [isLoadingYears, setIsLoadingYears] = useState(false);
  const [isLoadingPerformance, setIsLoadingPerformance] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 追蹤從快取載入的年份，避免重複呼叫 API
  const cachedYearRef = useRef<number | null>(
    initialCache.performance ? initialCache.selectedYear : null
  );

  // 追蹤當年度是否已取得過資料（防止無限迴圈）
  // aggregate 模式同樣套用此策略，避免重覆請求。
  const fetchedCurrentYearRef = useRef<boolean>(false);

  // 取得可用年份清單
  const fetchAvailableYears = useCallback(async () => {
    if (!isAggregate && !portfolioId) return;

    setIsLoadingYears(true);
    setError(null);

    try {
      const years = isAggregate
        ? await portfolioApi.getAggregateYears()
        : await portfolioApi.getAvailableYears(portfolioId!);

      setAvailableYears(years);
      localStorage.setItem(`perf_years_${cacheNamespace}`, JSON.stringify(years));

      // 若尚未選擇年份，自動選擇當年度
      if (!selectedYear && years.years.length > 0) {
        setSelectedYear(years.currentYear);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法載入可用年份');
    } finally {
      setIsLoadingYears(false);
    }
  }, [isAggregate, portfolioId, selectedYear, cacheNamespace]);

  // 計算指定年度的績效
  const calculatePerformance = useCallback(async (
    year: number,
    yearEndPrices?: Record<string, YearEndPriceInfo>,
    yearStartPrices?: Record<string, YearEndPriceInfo>
  ) => {
    if (!isAggregate && !portfolioId) return;

    const cacheKey = `perf_data_${cacheNamespace}_${year}`;
    const hasCachedData = !!localStorage.getItem(cacheKey);

    // Stale-while-revalidate：有快取時不顯示 loading，避免閃爍
    if (!hasCachedData) {
      setIsLoadingPerformance(true);
    }
    setError(null);

    try {
      const request = {
        year,
        yearEndPrices,
        yearStartPrices,
      };

      const result = isAggregate
        ? await portfolioApi.calculateAggregateYearPerformance(request)
        : await portfolioApi.calculateYearPerformance(portfolioId!, request);

      setPerformance(result);
      localStorage.setItem(cacheKey, JSON.stringify(result));
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法計算年度績效');
    } finally {
      setIsLoadingPerformance(false);
    }
  }, [isAggregate, portfolioId, cacheNamespace]);

  // 切換選擇的年份
  const handleSetSelectedYear = useCallback((year: number) => {
    const currentYear = new Date().getFullYear();

    // 切換回當年度時重設取得旗標，允許 stale-while-revalidate
    if (year === currentYear) {
      fetchedCurrentYearRef.current = false;
    }

    setSelectedYear(year);

    // 立即從快取載入該年度資料以避免閃爍
    try {
      const cached = localStorage.getItem(`perf_data_${cacheNamespace}_${year}`);
      if (cached) {
        setPerformance(JSON.parse(cached));
        cachedYearRef.current = year;
      } else {
        setPerformance(null);
        cachedYearRef.current = null;
      }
    } catch {
      setPerformance(null);
      cachedYearRef.current = null;
    }
  }, [cacheNamespace]);

  // 手動重新整理
  const refresh = useCallback(async () => {
    await fetchAvailableYears();
    if (selectedYear) {
      await calculatePerformance(selectedYear);
    }
  }, [fetchAvailableYears, selectedYear, calculatePerformance]);

  // 元件掛載時自動取得可用年份（背景更新）
  useEffect(() => {
    if (autoFetch && (isAggregate || !!portfolioId)) {
      fetchAvailableYears();
    }
  }, [autoFetch, isAggregate, portfolioId, fetchAvailableYears]);

  /**
   * 年份變更時自動計算績效
   *
   * 策略：
   * - 當年度（YTD）：若快取有完整資料（startValueSource 有值），
   *   不在此處呼叫 API，改由 Performance.tsx 的 autoFetchPrices 處理，
   *   以避免 API 在補價前回傳 null 導致閃爍
   * - 歷史年度：僅使用快取（資料來自資料庫，不會變動）
   */
  useEffect(() => {
    if (!selectedYear || !autoFetch || (!isAggregate && !portfolioId)) return;

    const currentYear = new Date().getFullYear();
    const isCurrentYear = selectedYear === currentYear;

    // 歷史年度：有快取則跳過 API
    if (!isCurrentYear && cachedYearRef.current === selectedYear && performance) {
      return;
    }

    // 當年度處理
    if (isCurrentYear) {
      // 已取得過則跳過
      if (fetchedCurrentYearRef.current) return;

      // 快取有完整資料則跳過，交由 autoFetchPrices 處理
      if (performance?.startValueSource != null && performance.startValueSource !== 0) {
        fetchedCurrentYearRef.current = true;
        return;
      }

      fetchedCurrentYearRef.current = true;
    }

    // 無快取或快取不完整時呼叫 API
    calculatePerformance(selectedYear);
  }, [selectedYear, autoFetch, isAggregate, portfolioId, calculatePerformance, performance]);

  return {
    availableYears,
    selectedYear,
    performance,
    isLoadingYears,
    isLoadingPerformance,
    error,
    setSelectedYear: handleSetSelectedYear,
    calculatePerformance,
    refresh,
  };
}
