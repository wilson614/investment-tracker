/**
 * useCapeData Hook
 * Fetches and manages CAPE (Cyclically Adjusted P/E) data state
 */

import { useState, useEffect, useCallback } from 'react';
import type { CapeDisplayItem } from '../types';
import {
  getCapeData,
  refreshCapeData,
  transformCapeData,
  getSelectedRegions,
  setSelectedRegions as saveSelectedRegions,
  getAvailableRegions,
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
  const [data, setData] = useState<CapeDisplayItem[] | null>(null);
  const [dataDate, setDataDate] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedRegions, setSelectedRegionsState] = useState<string[]>(getSelectedRegions);
  const [availableRegions, setAvailableRegions] = useState<{ key: string; label: string }[]>(getAvailableRegions);

  const fetchData = useCallback(async (forceRefresh = false) => {
    setIsLoading(true);
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
      setError(err instanceof Error ? err.message : '無法取得 CAPE 資料');
      setData(null);
      setDataDate(null);
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
    fetchData();
  }, [fetchData]);

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
