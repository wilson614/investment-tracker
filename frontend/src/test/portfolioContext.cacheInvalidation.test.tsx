import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ReactNode } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PortfolioProvider, resolveCacheInvalidationOptions, usePortfolio } from '../contexts/PortfolioContext';
import { portfolioApi } from '../services/api';

vi.mock('../services/api', () => ({
  portfolioApi: {
    getAll: vi.fn(),
  },
}));

const authState = vi.hoisted(() => ({
  user: null as { id: string } | null,
}));

vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({
    user: authState.user,
  }),
}));

const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <PortfolioProvider>{children}</PortfolioProvider>
    </QueryClientProvider>
  );
}

describe('PortfolioContext invalidateSharedCaches options semantics', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    authState.user = null;
    mockedPortfolioApi.getAll.mockResolvedValue([]);
  });

  it('resolveCacheInvalidationOptions keeps backward-compatible defaults for partial options', () => {
    expect(resolveCacheInvalidationOptions()).toEqual({
      performance: true,
      assets: true,
      clearPerformanceStorage: true,
    });
    expect(resolveCacheInvalidationOptions({ performance: false })).toEqual({
      performance: false,
      assets: true,
      clearPerformanceStorage: true,
    });
    expect(resolveCacheInvalidationOptions({ assets: false })).toEqual({
      performance: true,
      assets: false,
      clearPerformanceStorage: true,
    });
    expect(resolveCacheInvalidationOptions({ clearPerformanceStorage: false })).toEqual({
      performance: true,
      assets: true,
      clearPerformanceStorage: false,
    });
    expect(
      resolveCacheInvalidationOptions({ performance: false, assets: false, clearPerformanceStorage: false })
    ).toEqual({
      performance: false,
      assets: false,
      clearPerformanceStorage: false,
    });
  });

  it('invalidateSharedCaches with partial options still invalidates performance cache but not assets query', () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });
    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    localStorage.setItem('perf_years_aggregate', JSON.stringify({ years: [2025] }));
    localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));

    const { result } = renderHook(() => usePortfolio(), { wrapper: createWrapper(queryClient) });

    expect(result.current.performanceVersion).toBe(0);

    act(() => {
      result.current.invalidateSharedCaches({ assets: false });
    });

    expect(localStorage.getItem('perf_years_aggregate')).toBeNull();
    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
    expect(invalidateQueriesSpy).not.toHaveBeenCalled();
    expect(result.current.performanceVersion).toBe(1);
  });

  it('selectPortfolio keeps existing perf localStorage cache while still resetting performance UI version', async () => {
    authState.user = { id: 'user-1' };

    mockedPortfolioApi.getAll.mockResolvedValue([
      {
        id: 'portfolio-a',
        baseCurrency: 'USD',
        homeCurrency: 'TWD',
        isActive: true,
        boundCurrencyLedgerId: 'ledger-a',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: 'portfolio-b',
        baseCurrency: 'USD',
        homeCurrency: 'TWD',
        isActive: true,
        boundCurrencyLedgerId: 'ledger-b',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]);

    localStorage.setItem('selected_portfolio_id', 'portfolio-a');
    localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
    localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
    localStorage.setItem('perf_years_portfolio-b', JSON.stringify({ years: [2025] }));
    localStorage.setItem('perf_data_portfolio-b_2025', JSON.stringify({ year: 2025 }));
    localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });
    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => usePortfolio(), { wrapper: createWrapper(queryClient) });

    await waitFor(() => {
      expect(result.current.currentPortfolioId).toBe('portfolio-a');
    });

    act(() => {
      result.current.selectPortfolio('portfolio-b');
    });

    expect(result.current.currentPortfolioId).toBe('portfolio-b');
    expect(result.current.performanceVersion).toBe(1);
    expect(localStorage.getItem('selected_portfolio_id')).toBe('portfolio-b');

    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    expect(localStorage.getItem('perf_years_portfolio-b')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-b_2025')).not.toBeNull();
    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();

    act(() => {
      result.current.selectPortfolio('portfolio-a');
    });

    expect(result.current.currentPortfolioId).toBe('portfolio-a');
    expect(result.current.performanceVersion).toBe(2);

    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    expect(localStorage.getItem('perf_years_portfolio-b')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-b_2025')).not.toBeNull();
    expect(invalidateQueriesSpy).not.toHaveBeenCalled();
  });

  it('invalidateSharedCaches default path still clears perf cache and invalidates assets query', () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });
    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
    localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
    localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));

    const { result } = renderHook(() => usePortfolio(), { wrapper: createWrapper(queryClient) });

    expect(result.current.performanceVersion).toBe(0);

    act(() => {
      result.current.invalidateSharedCaches();
    });

    expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
    expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['assets', 'summary'] });
    expect(result.current.performanceVersion).toBe(1);
  });
});
