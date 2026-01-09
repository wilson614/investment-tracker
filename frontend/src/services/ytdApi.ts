/**
 * YTD (Year-to-Date) API Service
 * Fetches market YTD comparison data via backend with localStorage caching
 */

import type { MarketYtdComparison } from '../types';
import { marketDataApi } from './api';

const YTD_CACHE_KEY = 'ytd_data_cache';
const YTD_CACHE_MAX_AGE = 5 * 60 * 1000; // 5 minutes (prices change frequently)

// Market key display names (Chinese)
const MARKET_DISPLAY_NAMES: Record<string, string> = {
  'All Country': '全球',
  'US Large': '美國大型',
  'Taiwan 0050': '台灣 0050',
  'Emerging Markets': '新興市場',
};

// Cache the last fetched data in memory for quick access
let cachedData: MarketYtdComparison | null = null;

/**
 * Load YTD data from localStorage cache
 */
export function loadCachedYtdData(): MarketYtdComparison | null {
  try {
    const cached = localStorage.getItem(YTD_CACHE_KEY);
    if (cached) {
      const { data, timestamp } = JSON.parse(cached);
      const age = Date.now() - timestamp;
      if (age <= YTD_CACHE_MAX_AGE) {
        cachedData = data;
        return data;
      }
    }
  } catch {
    // Ignore cache errors
  }
  return null;
}

/**
 * Save YTD data to localStorage cache
 */
function saveYtdDataToCache(data: MarketYtdComparison): void {
  try {
    localStorage.setItem(YTD_CACHE_KEY, JSON.stringify({
      data,
      timestamp: Date.now(),
    }));
    cachedData = data;
  } catch {
    // localStorage might be full or disabled
  }
}

/**
 * Transform raw YTD data to display-friendly format (Chinese names)
 */
export function transformYtdData(data: MarketYtdComparison): MarketYtdComparison {
  return {
    ...data,
    benchmarks: data.benchmarks.map((item) => ({
      ...item,
      marketKey: MARKET_DISPLAY_NAMES[item.marketKey] || item.marketKey,
    })),
  };
}

/**
 * Fetch YTD data from backend API
 */
export async function getYtdData(): Promise<MarketYtdComparison> {
  const data = await marketDataApi.getYtdComparison();
  saveYtdDataToCache(data);
  return data;
}

/**
 * Force refresh YTD data from backend API
 */
export async function refreshYtdData(): Promise<MarketYtdComparison> {
  const data = await marketDataApi.refreshYtdComparison();
  saveYtdDataToCache(data);
  return data;
}

/**
 * Get cached YTD data (memory cache)
 */
export function getCachedYtdData(): MarketYtdComparison | null {
  return cachedData;
}
