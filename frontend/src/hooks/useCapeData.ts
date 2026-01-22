/**
 * useCapeData Hook
 * Fetches and manages CAPE (Cyclically Adjusted P/E) data state
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import type { CapeDisplayItem } from '../types';
import {
  getCapeData,
  refreshCapeData,
  transformCapeData,
  getSelectedRegions,
  setSelectedRegions as saveSelectedRegions,
  getAvailableRegions,
  loadCachedCapeData,
  loadSelectedRegionsFromApi,
} from '../services/capeApi';

interface UseCapeDataResult {
  data: CapeDisplayItem[] | null;
  dataDate: string | null;
  isLoading: boolean;
  error: string | null;
  selectedRegions: string[];
  availableRegions: { key: string; label: string }[];
  setSelectedRegions: (regions: string[]) => void;
  refresh: () => Promise<void>;
}

export function useCapeData(): UseCapeDataResult {
  // Try to load cached data immediately for instant rendering
  const cachedCapeData = loadCachedCapeData();
  const initialData = cachedCapeData ? transformCapeData(cachedCapeData, getSelectedRegions()) : null;
  const initialDate = cachedCapeData?.date ?? null;

  const [data, setData] = useState<CapeDisplayItem[] | null>(initialData);
  const [dataDate, setDataDate] = useState<string | null>(initialDate);
  const [isLoading, setIsLoading] = useState(!cachedCapeData); // Only loading if no cache
  const [error, setError] = useState<string | null>(null);
  const [selectedRegions, setSelectedRegionsState] = useState<string[]>(getSelectedRegions);
  const [availableRegions, setAvailableRegions] = useState<{ key: string; label: string }[]>(getAvailableRegions);

  const hasFetchedRef = useRef(false);
  const hasLoadedPrefsRef = useRef(false);
  const dataRef = useRef(data);
  dataRef.current = data;

  // Load preferences from API on mount
  useEffect(() => {
    if (!hasLoadedPrefsRef.current) {
      hasLoadedPrefsRef.current = true;
      loadSelectedRegionsFromApi().then((regions) => {
        setSelectedRegionsState(regions);
      });
    }
  }, []);

  const fetchData = useCallback(async (forceRefresh = false) => {
    // Only show loading state if we have no data yet
    if (!dataRef.current) {
      setIsLoading(true);
    }
    setError(null);

    try {
      const capeData = forceRefresh
        ? await refreshCapeData()
        : await getCapeData();
      const displayItems = transformCapeData(capeData, selectedRegions);

      setData(displayItems);
      setDataDate(capeData.date);
      // Update available regions from fresh data
      setAvailableRegions(getAvailableRegions());
    } catch (err) {
      // Only set error if we don't have cached data to show
      if (!dataRef.current) {
        setError(err instanceof Error ? err.message : '無法取得 CAPE 資料');
        setData(null);
        setDataDate(null);
      }
      // If we have cached data, silently fail and keep showing cached data
    } finally {
      setIsLoading(false);
    }
  }, [selectedRegions]);

  const refresh = useCallback(async () => {
    await fetchData(true);
  }, [fetchData]);

  const handleSetSelectedRegions = useCallback((regions: string[]) => {
    setSelectedRegionsState(regions);
    saveSelectedRegions(regions);
  }, []);

  useEffect(() => {
    // Only fetch once on mount
    if (!hasFetchedRef.current) {
      hasFetchedRef.current = true;
      fetchData();
    }
  }, [fetchData]);

  // Re-transform data when selectedRegions changes
  useEffect(() => {
    const cached = loadCachedCapeData();
    if (cached) {
      setData(transformCapeData(cached, selectedRegions));
    }
  }, [selectedRegions]);

  return {
    data,
    dataDate,
    isLoading,
    error,
    selectedRegions,
    availableRegions,
    setSelectedRegions: handleSetSelectedRegions,
    refresh,
  };
}
