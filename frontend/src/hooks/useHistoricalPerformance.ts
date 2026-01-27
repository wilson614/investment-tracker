import { useState, useEffect, useCallback, useRef } from 'react';
import { portfolioApi } from '../services/api';
import type { AvailableYears, YearPerformance, YearEndPriceInfo } from '../types';

interface UseHistoricalPerformanceOptions {
  portfolioId: string; // 必填，父元件透過條件渲染確保有值
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
 * 從 localStorage 載入指定投資組合的快取資料
 */
function loadCacheForPortfolio(portfolioId: string): {
  availableYears: AvailableYears | null;
  selectedYear: number | null;
  performance: YearPerformance | null;
} {
  try {
    const cachedYears = localStorage.getItem(`perf_years_${portfolioId}`);
    if (cachedYears) {
      const years = JSON.parse(cachedYears) as AvailableYears;
      const year = years.currentYear ?? years.years?.[0] ?? null;
      if (year) {
        const cachedPerf = localStorage.getItem(`perf_data_${portfolioId}_${year}`);
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
  autoFetch = true,
}: UseHistoricalPerformanceOptions): UseHistoricalPerformanceResult {
  // 元件掛載時一次性載入所有快取資料，確保同步執行以避免閃爍
  const [initialCache] = useState(() => loadCacheForPortfolio(portfolioId));

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
  const fetchedCurrentYearRef = useRef<boolean>(false);

  // 取得可用年份清單
  const fetchAvailableYears = useCallback(async () => {
    if (!portfolioId) return;

    setIsLoadingYears(true);
    setError(null);

    try {
      const years = await portfolioApi.getAvailableYears(portfolioId);
      setAvailableYears(years);
      localStorage.setItem(`perf_years_${portfolioId}`, JSON.stringify(years));

      // 若尚未選擇年份，自動選擇當年度
      if (!selectedYear && years.years.length > 0) {
        setSelectedYear(years.currentYear);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法載入可用年份');
    } finally {
      setIsLoadingYears(false);
    }
  }, [portfolioId, selectedYear]);

  // 計算指定年度的績效
  const calculatePerformance = useCallback(async (
    year: number,
    yearEndPrices?: Record<string, YearEndPriceInfo>,
    yearStartPrices?: Record<string, YearEndPriceInfo>
  ) => {
    if (!portfolioId) return;

    const cacheKey = `perf_data_${portfolioId}_${year}`;
    const hasCachedData = !!localStorage.getItem(cacheKey);

    // Stale-while-revalidate：有快取時不顯示 loading，避免閃爍
    if (!hasCachedData) {
      setIsLoadingPerformance(true);
    }
    setError(null);

    try {
      const result = await portfolioApi.calculateYearPerformance(portfolioId, {
        year,
        yearEndPrices,
        yearStartPrices,
      });
      setPerformance(result);
      localStorage.setItem(cacheKey, JSON.stringify(result));
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法計算年度績效');
    } finally {
      setIsLoadingPerformance(false);
    }
  }, [portfolioId]);

  // 切換選擇的年份
  const handleSetSelectedYear = useCallback((year: number) => {
    const currentYear = new Date().getFullYear();

    // 切換回當年度時重設取得旗標，允許 stale-while-revalidate
    if (year === currentYear) {
      fetchedCurrentYearRef.current = false;
    }

    setSelectedYear(year);

    // 立即從快取載入該年度資料以避免閃爍
    if (portfolioId) {
      try {
        const cached = localStorage.getItem(`perf_data_${portfolioId}_${year}`);
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
    }
  }, [portfolioId]);

  // 手動重新整理
  const refresh = useCallback(async () => {
    await fetchAvailableYears();
    if (selectedYear) {
      await calculatePerformance(selectedYear);
    }
  }, [fetchAvailableYears, selectedYear, calculatePerformance]);

  // 元件掛載時自動取得可用年份（背景更新）
  useEffect(() => {
    if (autoFetch && portfolioId) {
      fetchAvailableYears();
    }
  }, [autoFetch, portfolioId, fetchAvailableYears]);

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
    if (!selectedYear || !portfolioId || !autoFetch) return;

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
  }, [selectedYear, portfolioId, autoFetch, calculatePerformance, performance]);

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
