import { createContext, useContext, useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { Portfolio } from '../types';
import { portfolioApi } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { ASSETS_KEYS } from '../features/total-assets/hooks/useTotalAssets';
import {
  invalidatePerformanceAndAssetsCaches,
  invalidatePerformanceLocalStorageCache,
} from '../utils/cacheInvalidation';

/**
 * invalidateSharedCaches 的部分參數語意：
 * - 未傳入 options 時，預設同時清理 performance 與 assets（維持既有行為）
 * - 只傳入其中一個欄位時，另一個欄位仍預設為 true
 * - clearPerformanceStorage=false 時，只重置 Performance UI 狀態，不清 localStorage 的 perf cache
 */
export interface CacheInvalidationOptions {
  performance?: boolean;
  assets?: boolean;
  clearPerformanceStorage?: boolean;
}

export const DEFAULT_CACHE_INVALIDATION_OPTIONS: Readonly<Required<CacheInvalidationOptions>> = {
  performance: true,
  assets: true,
  clearPerformanceStorage: true,
};

export function resolveCacheInvalidationOptions(
  options: CacheInvalidationOptions = {},
): Readonly<Required<CacheInvalidationOptions>> {
  return {
    performance: options.performance ?? DEFAULT_CACHE_INVALIDATION_OPTIONS.performance,
    assets: options.assets ?? DEFAULT_CACHE_INVALIDATION_OPTIONS.assets,
    clearPerformanceStorage: options.clearPerformanceStorage
      ?? DEFAULT_CACHE_INVALIDATION_OPTIONS.clearPerformanceStorage,
  };
}

interface PortfolioContextValue {
  // Current selected portfolio
  currentPortfolio: Portfolio | null;
  currentPortfolioId: string | null;
  isAllPortfolios: boolean;

  // All portfolios
  portfolios: Portfolio[];
  isLoading: boolean;

  // Actions
  selectPortfolio: (portfolioId: string) => void;
  refreshPortfolios: () => Promise<void>;

  // Performance state management
  clearPerformanceState: () => void;
  invalidateSharedCaches: (options?: CacheInvalidationOptions) => void;
  performanceVersion: number; // Increment to trigger re-fetch
}

const PortfolioContext = createContext<PortfolioContextValue | null>(null);

// localStorage key for persisting selected portfolio
const SELECTED_PORTFOLIO_KEY = 'selected_portfolio_id';

export function PortfolioProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [currentPortfolioId, setCurrentPortfolioId] = useState<string | null>(() => {
    // Load from localStorage on init
    try {
      return localStorage.getItem(SELECTED_PORTFOLIO_KEY) ?? 'all';
    } catch {
      return 'all';
    }
  });
  const [isLoading, setIsLoading] = useState(true);
  const [performanceVersion, setPerformanceVersion] = useState(0);

  // Ref to track current ID for refresh logic without triggering re-creation
  const currentPortfolioIdRef = useRef(currentPortfolioId);
  useEffect(() => {
    currentPortfolioIdRef.current = currentPortfolioId;
  }, [currentPortfolioId]);

  const isAllPortfolios = currentPortfolioId === 'all';
  const currentPortfolio = isAllPortfolios
    ? null
    : portfolios.find(p => p.id === currentPortfolioId) ?? null;

  const refreshPortfolios = useCallback(async () => {
    try {
      setIsLoading(true);
      const data = await portfolioApi.getAll();
      setPortfolios(data);

      // Keep existing selection if valid or "all", otherwise fallback to "all"
      const currentId = currentPortfolioIdRef.current;
      if (data.length > 0) {
        const isAllSelection = currentId === 'all';
        const selectedExists = !!currentId && data.some(p => p.id === currentId);

        if (!isAllSelection && !selectedExists) {
          setCurrentPortfolioId('all');
          localStorage.setItem(SELECTED_PORTFOLIO_KEY, 'all');
        }
      } else {
        setCurrentPortfolioId('all');
        localStorage.setItem(SELECTED_PORTFOLIO_KEY, 'all');
      }
    } catch (error) {
      console.error('Failed to load portfolios:', error);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const invalidateSharedCaches = useCallback((options: CacheInvalidationOptions = {}) => {
    const {
      performance: shouldInvalidatePerformance,
      assets: shouldInvalidateAssets,
      clearPerformanceStorage,
    } = resolveCacheInvalidationOptions(options);

    const shouldClearPerformanceStorage = shouldInvalidatePerformance && clearPerformanceStorage;

    if (shouldClearPerformanceStorage && shouldInvalidateAssets) {
      invalidatePerformanceAndAssetsCaches(queryClient, ASSETS_KEYS.summaryQuery);
    } else {
      if (shouldClearPerformanceStorage) {
        invalidatePerformanceLocalStorageCache();
      }

      if (shouldInvalidateAssets) {
        void queryClient.invalidateQueries({ queryKey: ASSETS_KEYS.summaryQuery }).catch(() => undefined);
      }
    }

    if (shouldInvalidatePerformance) {
      setPerformanceVersion(v => v + 1);
    }
  }, [queryClient]);

  const selectPortfolio = useCallback((portfolioId: string) => {
    if (portfolioId === currentPortfolioId) return;

    // 僅重置 Performance UI 狀態，不清除既有績效 localStorage 快取
    // 讓 A↔B↔A 可復用各投組已存在的 perf_data_* / perf_years_* cache
    invalidateSharedCaches({ performance: true, assets: false, clearPerformanceStorage: false });

    // Update selection
    setCurrentPortfolioId(portfolioId);
    try {
      localStorage.setItem(SELECTED_PORTFOLIO_KEY, portfolioId);
    } catch {
      // Ignore localStorage errors
    }
  }, [currentPortfolioId, invalidateSharedCaches]);

  const clearPerformanceState = useCallback(() => {
    invalidateSharedCaches({ performance: true, assets: false });
  }, [invalidateSharedCaches]);

  // Load portfolios on mount - only when user is logged in
  useEffect(() => {
    if (user) {
      refreshPortfolios();
    } else {
      // Clear state when user logs out
      setPortfolios([]);
      setCurrentPortfolioId('all');
      setIsLoading(false);
    }
  }, [user, refreshPortfolios]);

  return (
    <PortfolioContext.Provider
      value={{
        currentPortfolio,
        currentPortfolioId,
        isAllPortfolios,
        portfolios,
        isLoading,
        selectPortfolio,
        refreshPortfolios,
        clearPerformanceState,
        invalidateSharedCaches,
        performanceVersion,
      }}
    >
      {children}
    </PortfolioContext.Provider>
  );
}

export function usePortfolio() {
  const context = useContext(PortfolioContext);
  if (!context) {
    throw new Error('usePortfolio must be used within a PortfolioProvider');
  }
  return context;
}
