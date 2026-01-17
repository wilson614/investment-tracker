/**
 * YTD (Year-to-Date) API Service
 * Fetches market YTD comparison data via backend with localStorage caching
 */

import type { MarketYtdComparison, MarketYtdReturn } from '../types';
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

const DISPLAY_NAME_TO_MARKET_KEY: Record<string, string> = Object.fromEntries(
  Object.entries(MARKET_DISPLAY_NAMES).map(([marketKey, displayName]) => [displayName, marketKey])
);

function normalizeMarketKey(marketKey: string): { marketKey: string; changed: boolean } {
  const mapped = DISPLAY_NAME_TO_MARKET_KEY[marketKey];
  if (mapped) return { marketKey: mapped, changed: true };
  return { marketKey, changed: false };
}

function normalizeYtdComparison(data: MarketYtdComparison): { data: MarketYtdComparison; changed: boolean } {
  let changed = false;

  const normalizedBenchmarks: MarketYtdReturn[] = data.benchmarks.map((item) => {
    const normalized = normalizeMarketKey(item.marketKey);
    if (normalized.changed) changed = true;
    return normalized.changed ? { ...item, marketKey: normalized.marketKey } : item;
  });

  return {
    data: changed ? { ...data, benchmarks: normalizedBenchmarks } : data,
    changed,
  };
}

// Cache the last fetched data in memory for quick access
let cachedData: MarketYtdComparison | null = null;

export interface CachedYtdResult {
  data: MarketYtdComparison | null;
  isStale: boolean;
  needsMigration: boolean;
}

/**
 * Load YTD data from localStorage cache (stale-while-revalidate)
 * Returns data even if stale, with isStale flag
 */
export function loadCachedYtdData(): CachedYtdResult {
  try {
    const cached = localStorage.getItem(YTD_CACHE_KEY);
    if (cached) {
      const { data, timestamp } = JSON.parse(cached) as {
        data: MarketYtdComparison;
        timestamp: number;
      };

      const normalized = normalizeYtdComparison(data);
      if (normalized.changed) {
        // Migration: persist normalized keys so subsequent loads are consistent.
        saveYtdDataToCache(normalized.data);
      } else {
        cachedData = data;
      }

      const age = Date.now() - timestamp;
      return {
        data: normalized.data,
        isStale: age > YTD_CACHE_MAX_AGE,
        needsMigration: normalized.changed,
      };
    }
  } catch {
    // Ignore cache errors
  }
  return { data: null, isStale: true, needsMigration: false };
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
  // Ensure marketKey stays stable (English) for filtering/preferences.
  return normalizeYtdComparison(data).data;
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
