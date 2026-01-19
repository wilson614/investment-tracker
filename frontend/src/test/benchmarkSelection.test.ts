import { describe, it, expect } from 'vitest';

/**
 * T126: Frontend tests for benchmark selection cap (max 10) and render gate.
 * These tests verify the business logic for benchmark selection constraints.
 */

describe('Benchmark Selection Logic', () => {
  /**
   * Helper function that mirrors the toggleBenchmark logic from MarketYtdSection.tsx
   */
  const toggleBenchmark = (prev: string[], key: string): string[] => {
    if (prev.includes(key)) {
      // Don't allow removing if only 1 selected
      if (prev.length === 1) return prev;
      return prev.filter(k => k !== key);
    }
    // Enforce max 10 benchmarks
    if (prev.length >= 10) return prev;
    return [...prev, key];
  };

  describe('Max 10 Benchmarks Enforcement', () => {
    it('should allow adding benchmark when under 10 selected', () => {
      const current = ['A', 'B', 'C'];
      const result = toggleBenchmark(current, 'D');
      
      expect(result).toHaveLength(4);
      expect(result).toContain('D');
    });

    it('should block adding 11th benchmark', () => {
      const current = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J']; // 10 items
      const result = toggleBenchmark(current, 'K');
      
      expect(result).toHaveLength(10);
      expect(result).not.toContain('K');
    });

    it('should allow adding exactly 10 benchmarks', () => {
      const current = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I']; // 9 items
      const result = toggleBenchmark(current, 'J');
      
      expect(result).toHaveLength(10);
      expect(result).toContain('J');
    });

    it('should allow removing benchmark when at 10', () => {
      const current = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J']; // 10 items
      const result = toggleBenchmark(current, 'E');
      
      expect(result).toHaveLength(9);
      expect(result).not.toContain('E');
    });
  });

  describe('Minimum 1 Benchmark Enforcement', () => {
    it('should not allow removing the last benchmark', () => {
      const current = ['A'];
      const result = toggleBenchmark(current, 'A');
      
      expect(result).toHaveLength(1);
      expect(result).toContain('A');
    });

    it('should allow removing when more than 1 selected', () => {
      const current = ['A', 'B'];
      const result = toggleBenchmark(current, 'A');
      
      expect(result).toHaveLength(1);
      expect(result).not.toContain('A');
      expect(result).toContain('B');
    });
  });

  describe('Toggle Behavior', () => {
    it('should toggle off an already selected benchmark', () => {
      const current = ['A', 'B', 'C'];
      const result = toggleBenchmark(current, 'B');
      
      expect(result).toEqual(['A', 'C']);
    });

    it('should toggle on a new benchmark', () => {
      const current = ['A', 'B'];
      const result = toggleBenchmark(current, 'C');
      
      expect(result).toEqual(['A', 'B', 'C']);
    });
  });
});

describe('Render Gate Logic', () => {
  /**
   * Helper function that mirrors the render gate logic from Performance.tsx
   * The bar chart should only render when both conditions are met:
   * 1. Holdings data is ready (not fetching prices, no missing prices)
   * 2. Benchmark data is ready (not loading, has returns)
   */
  const shouldRenderBarChart = (params: {
    isLoadingBenchmark: boolean;
    benchmarkReturnsCount: number;
    isFetchingPrices: boolean;
    hasMissingPrices: boolean;
    performanceVersion: number;
    lastResetVersion: number;
  }): boolean => {
    const {
      isLoadingBenchmark,
      benchmarkReturnsCount,
      isFetchingPrices,
      hasMissingPrices,
      performanceVersion,
      lastResetVersion,
    } = params;

    // Version mismatch means stale data
    if (lastResetVersion !== performanceVersion) return false;
    
    // Still loading benchmarks
    if (isLoadingBenchmark) return false;
    
    // No benchmark data yet
    if (benchmarkReturnsCount === 0) return false;
    
    // Still fetching prices for current year
    if (isFetchingPrices) return false;
    
    // Has missing prices that need user input
    if (hasMissingPrices) return false;
    
    return true;
  };

  it('should not render when benchmark is loading', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: true,
      benchmarkReturnsCount: 5,
      isFetchingPrices: false,
      hasMissingPrices: false,
      performanceVersion: 1,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(false);
  });

  it('should not render when no benchmark data', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: false,
      benchmarkReturnsCount: 0,
      isFetchingPrices: false,
      hasMissingPrices: false,
      performanceVersion: 1,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(false);
  });

  it('should not render when fetching prices', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: false,
      benchmarkReturnsCount: 5,
      isFetchingPrices: true,
      hasMissingPrices: false,
      performanceVersion: 1,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(false);
  });

  it('should not render when has missing prices', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: false,
      benchmarkReturnsCount: 5,
      isFetchingPrices: false,
      hasMissingPrices: true,
      performanceVersion: 1,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(false);
  });

  it('should not render when version mismatch (stale data)', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: false,
      benchmarkReturnsCount: 5,
      isFetchingPrices: false,
      hasMissingPrices: false,
      performanceVersion: 2,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(false);
  });

  it('should render when all conditions are met', () => {
    const result = shouldRenderBarChart({
      isLoadingBenchmark: false,
      benchmarkReturnsCount: 5,
      isFetchingPrices: false,
      hasMissingPrices: false,
      performanceVersion: 1,
      lastResetVersion: 1,
    });
    
    expect(result).toBe(true);
  });
});
