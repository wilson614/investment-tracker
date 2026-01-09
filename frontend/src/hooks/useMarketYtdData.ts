/**
 * useMarketYtdData Hook
 * Fetches and manages YTD (Year-to-Date) market data state
 * Implements stale-while-revalidate pattern to prevent UI flickering
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import type { MarketYtdComparison } from '../types';
import {
  getYtdData,
  refreshYtdData,
  transformYtdData,
  loadCachedYtdData,
} from '../services/ytdApi';

interface UseMarketYtdDataResult {
  data: MarketYtdComparison | null;
  isLoading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

export function useMarketYtdData(): UseMarketYtdDataResult {
  // Try to load cached data immediately for instant rendering
  const cachedYtdData = loadCachedYtdData();
  const initialData = cachedYtdData ? transformYtdData(cachedYtdData) : null;

  const [data, setData] = useState<MarketYtdComparison | null>(initialData);
  const [isLoading, setIsLoading] = useState(!cachedYtdData); // Only loading if no cache
  const [error, setError] = useState<string | null>(null);

  const hasFetchedRef = useRef(false);
  const dataRef = useRef(data);
  dataRef.current = data;

  const fetchData = useCallback(async (forceRefresh = false) => {
    // Only show loading state if we have no data yet
    if (!dataRef.current) {
      setIsLoading(true);
    }
    setError(null);

    try {
      const ytdData = forceRefresh
        ? await refreshYtdData()
        : await getYtdData();
      const transformedData = transformYtdData(ytdData);
      setData(transformedData);
    } catch (err) {
      // Only set error if we don't have cached data to show
      if (!dataRef.current) {
        setError(err instanceof Error ? err.message : '無法取得 YTD 資料');
        setData(null);
      }
      // If we have cached data, silently fail and keep showing cached data
    } finally {
      setIsLoading(false);
    }
  }, []);

  const refresh = useCallback(async () => {
    await fetchData(true);
  }, [fetchData]);

  useEffect(() => {
    // Only fetch once on mount
    if (!hasFetchedRef.current) {
      hasFetchedRef.current = true;
      fetchData();
    }
  }, [fetchData]);

  return {
    data,
    isLoading,
    error,
    refresh,
  };
}
