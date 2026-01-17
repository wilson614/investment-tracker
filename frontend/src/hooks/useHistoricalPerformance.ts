import { useState, useEffect, useCallback } from 'react';
import { portfolioApi } from '../services/api';
import type { AvailableYears, YearPerformance, CalculateYearPerformanceRequest, YearEndPriceInfo } from '../types';

interface UseHistoricalPerformanceOptions {
  portfolioId: string | undefined;
  autoFetch?: boolean;
  /** Version number - increment to force clear state */
  version?: number;
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

export function useHistoricalPerformance({
  portfolioId,
  autoFetch = true,
  version = 0,
}: UseHistoricalPerformanceOptions): UseHistoricalPerformanceResult {
  const [availableYears, setAvailableYears] = useState<AvailableYears | null>(null);
  const [selectedYear, setSelectedYear] = useState<number | null>(null);
  const [performance, setPerformance] = useState<YearPerformance | null>(null);
  const [isLoadingYears, setIsLoadingYears] = useState(false);
  const [isLoadingPerformance, setIsLoadingPerformance] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Clear state when version changes (portfolio switch)
  useEffect(() => {
    if (version > 0) {
      setAvailableYears(null);
      setSelectedYear(null);
      setPerformance(null);
      setError(null);
    }
  }, [version]);

  const fetchAvailableYears = useCallback(async () => {
    if (!portfolioId) return;

    setIsLoadingYears(true);
    setError(null);

    try {
      const years = await portfolioApi.getAvailableYears(portfolioId);
      setAvailableYears(years);

      // Auto-select current year if available
      if (!selectedYear && years.years.length > 0) {
        setSelectedYear(years.currentYear);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法載入可用年份');
    } finally {
      setIsLoadingYears(false);
    }
  }, [portfolioId, selectedYear]);

  const calculatePerformance = useCallback(async (
    year: number,
    yearEndPrices?: Record<string, YearEndPriceInfo>,
    yearStartPrices?: Record<string, YearEndPriceInfo>
  ) => {
    if (!portfolioId) return;

    setIsLoadingPerformance(true);
    setError(null);

    try {
      const request: CalculateYearPerformanceRequest = {
        year,
        yearEndPrices,
        yearStartPrices,
      };
      const result = await portfolioApi.calculateYearPerformance(portfolioId, request);
      setPerformance(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : '無法計算年度績效');
    } finally {
      setIsLoadingPerformance(false);
    }
  }, [portfolioId]);

  const handleSetSelectedYear = useCallback((year: number) => {
    setSelectedYear(year);
    setPerformance(null);
  }, []);

  const refresh = useCallback(async () => {
    await fetchAvailableYears();
    if (selectedYear) {
      await calculatePerformance(selectedYear);
    }
  }, [fetchAvailableYears, selectedYear, calculatePerformance]);

  // Auto-fetch available years on mount
  useEffect(() => {
    if (autoFetch && portfolioId) {
      fetchAvailableYears();
    }
  }, [autoFetch, portfolioId, fetchAvailableYears]);

  // Auto-calculate performance when year changes
  useEffect(() => {
    if (selectedYear && portfolioId && autoFetch) {
      calculatePerformance(selectedYear);
    }
  }, [selectedYear, portfolioId, autoFetch, calculatePerformance]);

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
