/**
 * YTD (Year-to-Date) API Service
 * Fetches market YTD comparison data via backend with localStorage caching
 */

import type { MarketYtdComparison } from '../types';
import { marketDataApi } from './api';

const YTD_CACHE_KEY = 'ytd_data_cache';
const YTD_CACHE_MAX_AGE = 5 * 60 * 1000; // 5 minutes - after this, data is stale but still usable

// Market key display names (Chinese)
const MARKET_DISPLAY_NAMES: Record<string, string> = {
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

// Cache the last fetched data in memory for quick access
let cachedData: MarketYtdComparison | null = null;

export interface CachedYtdResult {
  data: MarketYtdComparison | null;
  isStale: boolean;
}

/**
 * Load YTD data from localStorage cache (stale-while-revalidate)
 * Returns data even if stale, with isStale flag
 */
export function loadCachedYtdData(): CachedYtdResult {
  try {
    const cached = localStorage.getItem(YTD_CACHE_KEY);
    if (cached) {
      const { data, timestamp } = JSON.parse(cached);
      const age = Date.now() - timestamp;
      cachedData = data;
      return {
        data,
        isStale: age > YTD_CACHE_MAX_AGE,
      };
    }
  } catch {
    // Ignore cache errors
  }
  return { data: null, isStale: true };
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
