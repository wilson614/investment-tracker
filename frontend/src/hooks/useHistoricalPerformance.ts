import { useState, useEffect, useCallback, useRef } from 'react';
import { portfolioApi } from '../services/api';
import type { AvailableYears, YearPerformance, YearEndPriceInfo } from '../types';
import {
  buildPerformanceDataCacheKey,
  buildPerformanceYearsCacheKey,
  getPerformanceCacheVersion,
} from '../utils/cacheInvalidation';

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

const GLOBAL_SELECTED_YEAR_STORAGE_KEY = 'perf_selected_year';

function loadGlobalSelectedYear(): number | null {
  try {
    const raw = localStorage.getItem(GLOBAL_SELECTED_YEAR_STORAGE_KEY);
    if (!raw) return null;

    const parsed = Number(raw);
    return Number.isInteger(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function persistGlobalSelectedYear(year: number): void {
  try {
    localStorage.setItem(GLOBAL_SELECTED_YEAR_STORAGE_KEY, String(year));
  } catch {
    // 忽略快取寫入錯誤
  }
}

function resolveSelectedYear(
  years: AvailableYears,
  preferredYear: number | null
): number | null {
  if (!Array.isArray(years.years) || years.years.length === 0) {
    return null;
  }

  if (preferredYear != null && years.years.includes(preferredYear)) {
    return preferredYear;
  }

  if (years.currentYear != null && years.years.includes(years.currentYear)) {
    return years.currentYear;
  }

  return years.years[0] ?? null;
}

/**
 * 從 localStorage 載入指定命名空間的績效快取資料。
 * - 單一投資組合：namespace = portfolioId
 * - 彙總模式：namespace = aggregate
 */
function loadCacheForPortfolio(
  cacheNamespace: string,
  preferredYear: number | null = null,
  cacheVersion = 0,
): {
  availableYears: AvailableYears | null;
  selectedYear: number | null;
  performance: YearPerformance | null;
} {
  try {
    const yearsCacheKey = buildPerformanceYearsCacheKey(cacheNamespace, cacheVersion);
    const cachedYears = localStorage.getItem(yearsCacheKey);

    if (cachedYears) {
      const years = JSON.parse(cachedYears) as AvailableYears;
      const hasAvailableYears = Array.isArray(years.years) && years.years.length > 0;

      if (!hasAvailableYears) {
        return { availableYears: years, selectedYear: null, performance: null };
      }

      const year = resolveSelectedYear(years, preferredYear);
      if (year != null) {
        const performanceCacheKey = buildPerformanceDataCacheKey(cacheNamespace, year, cacheVersion);
        const cachedPerf = localStorage.getItem(performanceCacheKey);

        return {
          availableYears: years,
          selectedYear: year,
          performance: cachedPerf ? JSON.parse(cachedPerf) : null,
        };
      }

      return { availableYears: years, selectedYear: null, performance: null };
    }
  } catch {
    /* 忽略快取讀取錯誤 */
  }

  return { availableYears: null, selectedYear: null, performance: null };
}

interface ApiErrorLike {
  status?: number;
  message?: string;
}

const LEGACY_AGGREGATE_EMPTY_STATE_MESSAGES = new Set([
  'not found',
  'portfolio not found',
  'portfolio',
]);

function isLegacyAggregateEmptyStateError(error: unknown, isAggregate: boolean): boolean {
  if (!isAggregate || !error || typeof error !== 'object') {
    return false;
  }

  const apiError = error as ApiErrorLike;
  if (apiError.status !== 404) {
    return false;
  }

  const normalizedMessage = typeof apiError.message === 'string'
    ? apiError.message.trim().toLowerCase()
    : '';

  return LEGACY_AGGREGATE_EMPTY_STATE_MESSAGES.has(normalizedMessage);
}

function createEmptyAvailableYears(): AvailableYears {
  return {
    years: [],
    earliestYear: null,
    currentYear: new Date().getFullYear(),
  };
}

function isCompletePerformanceCache(performance: YearPerformance | null | undefined): performance is YearPerformance {
  return Boolean(performance && performance.isComplete === true);
}

export function useHistoricalPerformance({
  portfolioId,
  isAggregate = false,
  autoFetch = true,
}: UseHistoricalPerformanceOptions): UseHistoricalPerformanceResult {
  const cacheNamespace = isAggregate ? 'aggregate' : portfolioId;
  const performanceCacheVersion = getPerformanceCacheVersion();

  // 讀取跨投組共用的年份偏好，避免切換投組時被重設到最新年
  const [globalPreferredYear] = useState<number | null>(() => loadGlobalSelectedYear());

  // 元件掛載時一次性載入所有快取資料，確保同步執行以避免閃爍
  const [initialCache] = useState(() =>
    loadCacheForPortfolio(cacheNamespace, globalPreferredYear, performanceCacheVersion)
  );

  // 使用快取資料初始化狀態，實現即時顯示
  const [availableYears, setAvailableYears] = useState<AvailableYears | null>(
    initialCache.availableYears ?? (isAggregate ? createEmptyAvailableYears() : null)
  );
  const [selectedYear, setSelectedYear] = useState<number | null>(initialCache.selectedYear);
  const [performance, setPerformance] = useState<YearPerformance | null>(initialCache.performance);
  const [isLoadingYears, setIsLoadingYears] = useState(false);
  const [isLoadingPerformance, setIsLoadingPerformance] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 追蹤從快取載入的年份，避免重複呼叫 API
  const cachedYearRef = useRef<number | null>(
    initialCache.performance ? initialCache.selectedYear : null
  );

  // 追蹤目前已選擇的年份，供 callback 讀取最新值
  const selectedYearRef = useRef<number | null>(initialCache.selectedYear);

  // 追蹤 selectedYear 是否發生切換，用於重設歷史年度自動重算旗標
  const previousSelectedYearRef = useRef<number | null>(initialCache.selectedYear);

  // 追蹤歷史年度是否已自動重算，避免 isComplete=false 時迴圈重算
  const fetchedHistoricalYearRef = useRef<number | null>(null);

  // 追蹤最新的績效請求，避免舊回應覆寫最新年度資料
  const performanceRequestIdRef = useRef<number>(0);

  // 追蹤當年度是否已取得過資料（防止無限迴圈）
  // aggregate 模式同樣套用此策略，避免重覆請求。
  const fetchedCurrentYearRef = useRef<boolean>(false);

  useEffect(() => {
    if (previousSelectedYearRef.current !== selectedYear) {
      fetchedHistoricalYearRef.current = null;
      previousSelectedYearRef.current = selectedYear;
    }

    selectedYearRef.current = selectedYear;
  }, [selectedYear]);

  useEffect(() => {
    if (selectedYear != null) {
      persistGlobalSelectedYear(selectedYear);
    }
  }, [selectedYear]);

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

      const yearsCacheKey = buildPerformanceYearsCacheKey(cacheNamespace, performanceCacheVersion);
      localStorage.setItem(yearsCacheKey, JSON.stringify(years));

      if (years.years.length === 0) {
        selectedYearRef.current = null;
        setSelectedYear(null);
        setPerformance(null);
        cachedYearRef.current = null;
        fetchedCurrentYearRef.current = false;
        return;
      }

      // 保留使用者原本選擇；僅在不存在時 fallback 到合理預設（currentYear / years[0]）
      const preferredYear = selectedYearRef.current ?? globalPreferredYear;
      setSelectedYear((previousYear) => {
        const resolvedYear = resolveSelectedYear(years, preferredYear);

        if (resolvedYear == null) {
          selectedYearRef.current = null;
          setPerformance(null);
          cachedYearRef.current = null;
          return null;
        }

        if (resolvedYear === previousYear) {
          return previousYear;
        }

        try {
          const resolvedYearCacheKey = buildPerformanceDataCacheKey(
            cacheNamespace,
            resolvedYear,
            performanceCacheVersion,
          );
          const cached = localStorage.getItem(resolvedYearCacheKey);

          if (cached) {
            setPerformance(JSON.parse(cached));
            cachedYearRef.current = resolvedYear;
          } else {
            setPerformance(null);
            cachedYearRef.current = null;
          }
        } catch {
          setPerformance(null);
          cachedYearRef.current = null;
        }

        selectedYearRef.current = resolvedYear;
        return resolvedYear;
      });
    } catch (err) {
      if (isLegacyAggregateEmptyStateError(err, isAggregate)) {
        setAvailableYears(createEmptyAvailableYears());
        selectedYearRef.current = null;
        setSelectedYear(null);
        setPerformance(null);
        return;
      }

      setError(err instanceof Error ? err.message : '無法載入可用年份');
    } finally {
      setIsLoadingYears(false);
    }
  }, [
    isAggregate,
    portfolioId,
    cacheNamespace,
    globalPreferredYear,
    performanceCacheVersion,
  ]);

  // 計算指定年度的績效
  const calculatePerformance = useCallback(async (
    year: number,
    yearEndPrices?: Record<string, YearEndPriceInfo>,
    yearStartPrices?: Record<string, YearEndPriceInfo>
  ) => {
    if (!isAggregate && !portfolioId) return;

    const isRequestForCurrentSelection = selectedYearRef.current === year;
    const requestId = isRequestForCurrentSelection
      ? ++performanceRequestIdRef.current
      : performanceRequestIdRef.current;
    const cacheKey = buildPerformanceDataCacheKey(cacheNamespace, year, performanceCacheVersion);
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

      if (requestId !== performanceRequestIdRef.current || selectedYearRef.current !== year) {
        return;
      }

      setPerformance(result);
      localStorage.setItem(cacheKey, JSON.stringify(result));
      cachedYearRef.current = year;
    } catch (err) {
      if (requestId !== performanceRequestIdRef.current || selectedYearRef.current !== year) {
        return;
      }

      if (isLegacyAggregateEmptyStateError(err, isAggregate)) {
        setPerformance(null);
        return;
      }

      setError(err instanceof Error ? err.message : '無法計算年度績效');
    } finally {
      if (requestId === performanceRequestIdRef.current) {
        setIsLoadingPerformance(false);
      }
    }
  }, [isAggregate, portfolioId, cacheNamespace, performanceCacheVersion]);

  // 切換選擇的年份
  const handleSetSelectedYear = useCallback((year: number) => {
    const currentYear = new Date().getFullYear();

    // 切換回當年度時重設取得旗標，允許 stale-while-revalidate
    if (year === currentYear) {
      fetchedCurrentYearRef.current = false;
    }

    selectedYearRef.current = year;
    setSelectedYear(year);
    persistGlobalSelectedYear(year);

    // 立即從快取載入該年度資料以避免閃爍
    try {
      const selectedYearCacheKey = buildPerformanceDataCacheKey(cacheNamespace, year, performanceCacheVersion);
      const cached = localStorage.getItem(selectedYearCacheKey);

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
  }, [cacheNamespace, performanceCacheVersion]);

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
   * - 歷史年度：快取完整時直接使用；快取缺失/不完整時僅自動重算一次，避免迴圈重算
   */
  useEffect(() => {
    if (!selectedYear || !autoFetch || (!isAggregate && !portfolioId)) return;

    const currentYear = new Date().getFullYear();
    const isCurrentYear = selectedYear === currentYear;

    // 歷史年度：快取完整時跳過 API；快取不完整僅自動重算一次，避免迴圈請求
    if (!isCurrentYear) {
      if (cachedYearRef.current === selectedYear && isCompletePerformanceCache(performance)) {
        return;
      }

      if (fetchedHistoricalYearRef.current === selectedYear) {
        return;
      }

      fetchedHistoricalYearRef.current = selectedYear;
      calculatePerformance(selectedYear);
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
