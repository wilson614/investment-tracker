import type { QueryClient, QueryKey } from '@tanstack/react-query';

export const PERFORMANCE_CACHE_VERSION_STORAGE_KEY = 'perf_cache_version';

const PERFORMANCE_YEARS_CACHE_PREFIX = 'perf_years_';
const PERFORMANCE_DATA_CACHE_PREFIX = 'perf_data_';

/**
 * 專案內歷史績效 cache namespace schema：
 * - aggregate（彙總模式）
 * - portfolioId 風格字串（至少包含一個 `-`，例如 UUID 或測試用 portfolio-a）
 */
const PERFORMANCE_CACHE_NAMESPACE_PATTERN = '(?:aggregate|[A-Za-z0-9]+(?:-[A-Za-z0-9]+)+)';
const PERFORMANCE_CACHE_YEAR_PATTERN = '[1-9]\\d{3}';
const PERFORMANCE_CACHE_VERSION_PATTERN = 'v\\d+_';

const PERFORMANCE_YEARS_CACHE_KEY_REGEX = new RegExp(
  `^${PERFORMANCE_YEARS_CACHE_PREFIX}(?:${PERFORMANCE_CACHE_VERSION_PATTERN})?${PERFORMANCE_CACHE_NAMESPACE_PATTERN}$`,
);

const PERFORMANCE_DATA_CACHE_KEY_REGEX = new RegExp(
  `^${PERFORMANCE_DATA_CACHE_PREFIX}(?:${PERFORMANCE_CACHE_VERSION_PATTERN})?${PERFORMANCE_CACHE_NAMESPACE_PATTERN}_${PERFORMANCE_CACHE_YEAR_PATTERN}$`,
);

function parseCacheVersion(rawValue: string | null): number {
  const parsed = Number(rawValue);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : 0;
}

function isPerformanceCacheStorageKey(storageKey: string): boolean {
  return PERFORMANCE_YEARS_CACHE_KEY_REGEX.test(storageKey)
    || PERFORMANCE_DATA_CACHE_KEY_REGEX.test(storageKey);
}

export function getPerformanceCacheVersion(): number {
  try {
    return parseCacheVersion(localStorage.getItem(PERFORMANCE_CACHE_VERSION_STORAGE_KEY));
  } catch {
    return 0;
  }
}

export function buildPerformanceYearsCacheKey(cacheNamespace: string, cacheVersion = getPerformanceCacheVersion()): string {
  if (cacheVersion <= 0) {
    return `${PERFORMANCE_YEARS_CACHE_PREFIX}${cacheNamespace}`;
  }

  return `${PERFORMANCE_YEARS_CACHE_PREFIX}v${cacheVersion}_${cacheNamespace}`;
}

export function buildPerformanceDataCacheKey(
  cacheNamespace: string,
  year: number,
  cacheVersion = getPerformanceCacheVersion(),
): string {
  if (cacheVersion <= 0) {
    return `${PERFORMANCE_DATA_CACHE_PREFIX}${cacheNamespace}_${year}`;
  }

  return `${PERFORMANCE_DATA_CACHE_PREFIX}v${cacheVersion}_${cacheNamespace}_${year}`;
}

export function clearPerformanceLocalStorageCache(): void {
  try {
    const removableKeys: string[] = [];

    for (let index = 0; index < localStorage.length; index += 1) {
      const storageKey = localStorage.key(index);
      if (storageKey && isPerformanceCacheStorageKey(storageKey)) {
        removableKeys.push(storageKey);
      }
    }

    removableKeys.forEach((storageKey) => {
      localStorage.removeItem(storageKey);
    });
  } catch {
    // Ignore localStorage errors
  }
}

export function invalidatePerformanceLocalStorageCache(): number {
  const nextVersion = getPerformanceCacheVersion() + 1;

  try {
    localStorage.setItem(PERFORMANCE_CACHE_VERSION_STORAGE_KEY, String(nextVersion));
  } catch {
    // Ignore localStorage errors
  }

  clearPerformanceLocalStorageCache();

  return nextVersion;
}

export function invalidateAssetsSummaryQuery(
  queryClient: QueryClient,
  queryKey: QueryKey,
): void {
  void queryClient.invalidateQueries({ queryKey }).catch(() => undefined);
}

export function invalidatePerformanceAndAssetsCaches(
  queryClient: QueryClient,
  assetsSummaryQueryKey: QueryKey,
): void {
  invalidatePerformanceLocalStorageCache();
  invalidateAssetsSummaryQuery(queryClient, assetsSummaryQueryKey);
}
