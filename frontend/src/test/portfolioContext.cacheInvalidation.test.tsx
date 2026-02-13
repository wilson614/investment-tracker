import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ReactNode } from 'react';
import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PortfolioProvider, resolveCacheInvalidationOptions, usePortfolio } from '../contexts/PortfolioContext';
import { portfolioApi } from '../services/api';

vi.mock('../services/api', () => ({
  portfolioApi: {
    getAll: vi.fn(),
  },
}));

vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({
    user: null,
  }),
}));

const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });

describe('PortfolioContext invalidateSharedCaches options semantics', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    mockedPortfolioApi.getAll.mockResolvedValue([]);
  });

  it('resolveCacheInvalidationOptions keeps backward-compatible defaults for partial options', () => {
    expect(resolveCacheInvalidationOptions()).toEqual({ performance: true, assets: true });
    expect(resolveCacheInvalidationOptions({ performance: false })).toEqual({
      performance: false,
      assets: true,
    });
    expect(resolveCacheInvalidationOptions({ assets: false })).toEqual({
      performance: true,
      assets: false,
    });
    expect(resolveCacheInvalidationOptions({ performance: false, assets: false })).toEqual({
      performance: false,
      assets: false,
    });
  });

  it('invalidateSharedCaches with partial options only invalidates performance while keeping assets default semantics explicit', () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });
    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    localStorage.setItem('perf_years_aggregate', JSON.stringify({ years: [2025] }));
    localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));

    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>
        <PortfolioProvider>{children}</PortfolioProvider>
      </QueryClientProvider>
    );

    const { result } = renderHook(() => usePortfolio(), { wrapper });

    act(() => {
      result.current.invalidateSharedCaches({ assets: false });
    });

    // performance 預設 true：應清掉 perf cache
    expect(localStorage.getItem('perf_years_aggregate')).toBeNull();
    // 無關 key 不應受影響
    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
    // assets:false：不應呼叫 react-query assets invalidate
    expect(invalidateQueriesSpy).not.toHaveBeenCalled();
  });
});
