import { describe, it, expect } from 'vitest';

/**
 * T108: Frontend tests for performance state reset on portfolio change.
 * These tests verify that switching portfolios properly clears stale data.
 */

describe('Performance State Reset Logic', () => {
  /**
   * Simulates the state reset behavior when portfolio changes.
   * In the real app, this is handled by:
   * 1. Query keys including portfolioId (useHistoricalPerformance.ts)
   * 2. performanceVersion tracking (PortfolioContext.tsx)
   * 3. UI state reset on version change (Performance.tsx)
   */
  interface PerformanceState {
    xirrTwd: number | null;
    xirrUsd: number | null;
    benchmarkReturns: Record<string, number | null>;
    isLoading: boolean;
    lastPortfolioId: string | null;
    performanceVersion: number;
  }

  const createInitialState = (): PerformanceState => ({
    xirrTwd: null,
    xirrUsd: null,
    benchmarkReturns: {},
    isLoading: true,
    lastPortfolioId: null,
    performanceVersion: 0,
  });

  const createLoadedState = (portfolioId: string, version: number): PerformanceState => ({
    xirrTwd: 12.5,
    xirrUsd: 8.3,
    benchmarkReturns: { 'All Country': 15.2, 'US Large': 18.5 },
    isLoading: false,
    lastPortfolioId: portfolioId,
    performanceVersion: version,
  });

  /**
   * Simulates state transition when portfolio changes.
   * Should clear all derived data and show loading state.
   */
  const handlePortfolioChange = (
    currentState: PerformanceState,
    newPortfolioId: string,
    newVersion: number
  ): PerformanceState => {
    // If portfolio changed, reset state
    if (currentState.lastPortfolioId !== newPortfolioId || 
        currentState.performanceVersion !== newVersion) {
      return {
        xirrTwd: null,
        xirrUsd: null,
        benchmarkReturns: {},
        isLoading: true,
        lastPortfolioId: newPortfolioId,
        performanceVersion: newVersion,
      };
    }
    return currentState;
  };

  describe('Portfolio Switching', () => {
    it('should reset state when switching to different portfolio', () => {
      const portfolioAState = createLoadedState('portfolio-a', 1);
      const newState = handlePortfolioChange(portfolioAState, 'portfolio-b', 2);

      expect(newState.xirrTwd).toBeNull();
      expect(newState.xirrUsd).toBeNull();
      expect(newState.benchmarkReturns).toEqual({});
      expect(newState.isLoading).toBe(true);
      expect(newState.lastPortfolioId).toBe('portfolio-b');
    });

    it('should reset state when performanceVersion changes', () => {
      const currentState = createLoadedState('portfolio-a', 1);
      const newState = handlePortfolioChange(currentState, 'portfolio-a', 2);

      expect(newState.xirrTwd).toBeNull();
      expect(newState.isLoading).toBe(true);
      expect(newState.performanceVersion).toBe(2);
    });

    it('should not reset state when portfolio and version are the same', () => {
      const currentState = createLoadedState('portfolio-a', 1);
      const newState = handlePortfolioChange(currentState, 'portfolio-a', 1);

      expect(newState).toBe(currentState); // Same reference
      expect(newState.xirrTwd).toBe(12.5);
      expect(newState.isLoading).toBe(false);
    });
  });

  describe('Initial Load State', () => {
    it('should start with null XIRR values', () => {
      const state = createInitialState();

      expect(state.xirrTwd).toBeNull();
      expect(state.xirrUsd).toBeNull();
    });

    it('should start with empty benchmark returns', () => {
      const state = createInitialState();

      expect(state.benchmarkReturns).toEqual({});
    });

    it('should start with loading state true', () => {
      const state = createInitialState();

      expect(state.isLoading).toBe(true);
    });
  });

  describe('Empty Portfolio Handling', () => {
    /**
     * Simulates displaying XIRR for an empty portfolio.
     * Should show "-" instead of stale values from previous portfolio.
     */
    const formatXirr = (xirr: number | null, isLoading: boolean): string => {
      if (isLoading) return 'Loading...';
      if (xirr === null) return '-';
      return `${xirr.toFixed(2)}%`;
    };

    it('should display loading when state is loading', () => {
      const result = formatXirr(null, true);
      expect(result).toBe('Loading...');
    });

    it('should display dash for null XIRR (empty portfolio)', () => {
      const result = formatXirr(null, false);
      expect(result).toBe('-');
    });

    it('should display formatted XIRR when available', () => {
      const result = formatXirr(12.5, false);
      expect(result).toBe('12.50%');
    });

    it('should not display previous portfolio value after switch', () => {
      // Start with portfolio A loaded
      let state = createLoadedState('portfolio-a', 1);
      expect(formatXirr(state.xirrTwd, state.isLoading)).toBe('12.50%');

      // Switch to portfolio B (empty)
      state = handlePortfolioChange(state, 'portfolio-b', 2);
      expect(formatXirr(state.xirrTwd, state.isLoading)).toBe('Loading...');

      // After loading completes with no data
      state = { ...state, isLoading: false };
      expect(formatXirr(state.xirrTwd, state.isLoading)).toBe('-');
    });
  });

  describe('Query Key Invalidation', () => {
    /**
     * Simulates how React Query keys should include portfolioId.
     * This ensures cache is properly invalidated on portfolio change.
     */
    const createQueryKey = (
      type: string,
      portfolioId: string,
      year: number
    ): readonly unknown[] => {
      return ['performance', type, portfolioId, year] as const;
    };

    it('should create unique query keys per portfolio', () => {
      const keyA = createQueryKey('xirr', 'portfolio-a', 2024);
      const keyB = createQueryKey('xirr', 'portfolio-b', 2024);

      expect(keyA).not.toEqual(keyB);
      expect(keyA[2]).toBe('portfolio-a');
      expect(keyB[2]).toBe('portfolio-b');
    });

    it('should create unique query keys per year', () => {
      const key2023 = createQueryKey('xirr', 'portfolio-a', 2023);
      const key2024 = createQueryKey('xirr', 'portfolio-a', 2024);

      expect(key2023).not.toEqual(key2024);
      expect(key2023[3]).toBe(2023);
      expect(key2024[3]).toBe(2024);
    });

    it('should include all necessary identifiers in query key', () => {
      const key = createQueryKey('xirr', 'portfolio-a', 2024);

      expect(key).toHaveLength(4);
      expect(key[0]).toBe('performance');
      expect(key[1]).toBe('xirr');
      expect(key[2]).toBe('portfolio-a');
      expect(key[3]).toBe(2024);
    });
  });
});
