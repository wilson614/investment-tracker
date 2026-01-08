/**
 * CAPE (Cyclically Adjusted P/E) API Service
 * Fetches Global CAPE data via backend proxy with localStorage caching for region preferences
 */

import type { CapeData, CapeDisplayItem, CapeValuation } from '../types';
import { marketDataApi } from './api';

const REGION_PREFS_KEY = 'cape_region_preferences';

// All available regions from the API (based on actual API response)
const ALL_KNOWN_REGIONS = [
  'All Country',
  'US Large',
  'US Small',
  'Developed Markets Large',
  'Dev ex US Large',
  'Dev ex US Small',
  'Emerging Markets',
  'Europe',
  'Europe ex UK',
  'Asia ex Japan',
  'Japan',
  'United Kingdom',
  'Germany',
  'France',
  'Canada',
  'Australia',
  'China',
  'India',
  'Brazil',
  'South Korea',
  'Taiwan',
  'Hong Kong',
  'Italy',
  'Spain',
  'Sweden',
  'Switzerland',
  'Netherlands',
  'Mexico',
  'Indonesia',
  'Malaysia',
  'Thailand',
  'Poland',
  'South Africa',
  'Turkey',
  'Singapore',
  'Austria',
  'Belgium',
  'Denmark',
  'Finland',
  'Ireland',
  'Norway',
  'Portugal',
  'New Zealand',
  'Chile',
  'Colombia',
  'Czech Republic',
  'Egypt',
  'Hungary',
  'Israel',
  'Peru',
  'Philippines',
];

// Default regions for new users
const DEFAULT_REGIONS = ['All Country', 'US Large', 'Emerging Markets', 'Europe', 'Taiwan'];

// Region name mapping for display (Chinese)
const REGION_DISPLAY_NAMES: Record<string, string> = {
  'All Country': '全球',
  'US Large': '美國大型',
  'US Small': '美國小型',
  'Developed Markets Large': '已開發大型',
  'Developed Markets Small': '已開發小型',
  'Dev ex US Large': '已開發（非美）大型',
  'Dev ex US Small': '已開發（非美）小型',
  'Emerging Markets': '新興市場',
  'Europe': '歐洲',
  'Europe ex UK': '歐洲（非英）',
  'Asia ex Japan': '亞洲（非日）',
  'Japan': '日本',
  'United Kingdom': '英國',
  'Germany': '德國',
  'France': '法國',
  'Canada': '加拿大',
  'Australia': '澳洲',
  'China': '中國',
  'India': '印度',
  'Brazil': '巴西',
  'South Korea': '韓國',
  'Taiwan': '台灣',
  'Hong Kong': '香港',
  'Italy': '義大利',
  'Spain': '西班牙',
  'Sweden': '瑞典',
  'Switzerland': '瑞士',
  'Netherlands': '荷蘭',
  'Mexico': '墨西哥',
  'Indonesia': '印尼',
  'Malaysia': '馬來西亞',
  'Thailand': '泰國',
  'Poland': '波蘭',
  'South Africa': '南非',
  'Turkey': '土耳其',
  'Singapore': '新加坡',
  'Austria': '奧地利',
  'Belgium': '比利時',
  'Denmark': '丹麥',
  'Finland': '芬蘭',
  'Ireland': '愛爾蘭',
  'Norway': '挪威',
  'Portugal': '葡萄牙',
  'New Zealand': '紐西蘭',
  'Chile': '智利',
  'Colombia': '哥倫比亞',
  'Czech Republic': '捷克',
  'Egypt': '埃及',
  'Hungary': '匈牙利',
  'Israel': '以色列',
  'Peru': '秘魯',
  'Philippines': '菲律賓',
};

// Cache the last fetched data in memory for quick access to available regions
let cachedData: CapeData | null = null;

/**
 * Determine valuation level based on CAPE value compared to its historical percentiles
 * Uses the market's own historical data for comparison
 */
export function getCapeValuation(cape: number, range25th: number, range75th: number): CapeValuation {
  if (cape < range25th) return 'cheap';
  if (cape > range75th) return 'expensive';
  return 'fair';
}

/**
 * Fetch CAPE data from backend API
 */
export async function getCapeData(): Promise<CapeData> {
  const data = await marketDataApi.getCapeData();
  cachedData = data;
  return data;
}

/**
 * Force refresh CAPE data from backend API (clears backend cache)
 */
export async function refreshCapeData(): Promise<CapeData> {
  const data = await marketDataApi.refreshCapeData();
  cachedData = data;
  return data;
}

/**
 * Clear the local cache reference
 */
export function clearCapeCache(): void {
  cachedData = null;
}

/**
 * Transform raw CAPE data into display-friendly format
 * Filters to only show user-selected regions
 */
export function transformCapeData(data: CapeData, selectedRegions?: string[]): CapeDisplayItem[] {
  const regions = selectedRegions || getSelectedRegions();

  return data.items
    .filter((item) => regions.includes(item.boxName))
    .map((item) => ({
      region: REGION_DISPLAY_NAMES[item.boxName] || item.boxName,
      cape: item.currentValue,
      adjustedCape: item.adjustedValue,
      percentile: item.currentValuePercentile,
      valuation: getCapeValuation(item.adjustedValue ?? item.currentValue, item.range25th, item.range75th),
      median: item.range50th,
      range25th: item.range25th,
      range75th: item.range75th,
    }))
    .sort((a, b) => {
      // Sort by user's selection order
      const regionKeys = Object.entries(REGION_DISPLAY_NAMES);
      const aKey = regionKeys.find(([_, v]) => v === a.region)?.[0] || a.region;
      const bKey = regionKeys.find(([_, v]) => v === b.region)?.[0] || b.region;
      return regions.indexOf(aKey) - regions.indexOf(bKey);
    });
}

/**
 * Get user's selected CAPE regions from localStorage
 */
export function getSelectedRegions(): string[] {
  try {
    const stored = localStorage.getItem(REGION_PREFS_KEY);
    if (stored) {
      const regions = JSON.parse(stored);
      if (Array.isArray(regions) && regions.length > 0) {
        return regions;
      }
    }
  } catch {
    // Ignore parsing errors
  }
  return DEFAULT_REGIONS;
}

/**
 * Save user's selected CAPE regions to localStorage
 */
export function setSelectedRegions(regions: string[]): void {
  try {
    localStorage.setItem(REGION_PREFS_KEY, JSON.stringify(regions));
  } catch {
    // localStorage might be full or disabled
  }
}

/**
 * Get all available regions from cached CAPE data
 * Falls back to known regions if no data available
 */
export function getAvailableRegions(): { key: string; label: string }[] {
  const regionKeys = cachedData
    ? cachedData.items.map((item) => item.boxName)
    : ALL_KNOWN_REGIONS;

  return regionKeys.map((key) => ({
    key,
    label: REGION_DISPLAY_NAMES[key] || key,
  }));
}

/**
 * Get default regions
 */
export function getDefaultRegions(): string[] {
  return [...DEFAULT_REGIONS];
}
