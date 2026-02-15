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
   * Helper function that mirrors the selectedYear-bound render gate logic from Performance.tsx
   */
  const shouldRenderBarChart = (params: {
    selectedYear: number | null;
    selectedSystemBenchmarks: string[];
    selectedCustomBenchmarkIds: string[];
    benchmarkReturns: Record<string, number | null | undefined>;
    benchmarkReturnYears: Record<string, number | undefined>;
    customBenchmarkReturns: Record<string, number | null | undefined>;
    customBenchmarkReturnYears: Record<string, number | undefined>;
  }): boolean => {
    const {
      selectedYear,
      selectedSystemBenchmarks,
      selectedCustomBenchmarkIds,
      benchmarkReturns,
      benchmarkReturnYears,
      customBenchmarkReturns,
      customBenchmarkReturnYears,
    } = params;

    if (!selectedYear) return false;

    const allSystemBenchmarksReady = selectedSystemBenchmarks.length === 0 ||
      selectedSystemBenchmarks.every((key) => (
        benchmarkReturns[key] !== undefined &&
        benchmarkReturnYears[key] === selectedYear
      ));

    const allCustomBenchmarksReady = selectedCustomBenchmarkIds.length === 0 ||
      selectedCustomBenchmarkIds.every((id) => (
        customBenchmarkReturns[id] !== undefined &&
        customBenchmarkReturnYears[id] === selectedYear
      ));

    return allSystemBenchmarksReady && allCustomBenchmarksReady;
  };

  it('should not render when selectedYear is null', () => {
    const result = shouldRenderBarChart({
      selectedYear: null,
      selectedSystemBenchmarks: ['All Country'],
      selectedCustomBenchmarkIds: [],
      benchmarkReturns: { 'All Country': 10 },
      benchmarkReturnYears: { 'All Country': 2025 },
      customBenchmarkReturns: {},
      customBenchmarkReturnYears: {},
    });

    expect(result).toBe(false);
  });

  it('should not render when benchmark value exists but belongs to a different year', () => {
    const result = shouldRenderBarChart({
      selectedYear: 2024,
      selectedSystemBenchmarks: ['All Country'],
      selectedCustomBenchmarkIds: [],
      benchmarkReturns: { 'All Country': 12.5 },
      benchmarkReturnYears: { 'All Country': 2025 },
      customBenchmarkReturns: {},
      customBenchmarkReturnYears: {},
    });

    expect(result).toBe(false);
  });

  it('should not render when custom benchmark value exists but belongs to a different year', () => {
    const result = shouldRenderBarChart({
      selectedYear: 2024,
      selectedSystemBenchmarks: ['All Country'],
      selectedCustomBenchmarkIds: ['custom-1'],
      benchmarkReturns: { 'All Country': 8.8 },
      benchmarkReturnYears: { 'All Country': 2024 },
      customBenchmarkReturns: { 'custom-1': 6.6 },
      customBenchmarkReturnYears: { 'custom-1': 2025 },
    });

    expect(result).toBe(false);
  });

  it('should render only when both system/custom benchmark data match selectedYear', () => {
    const result = shouldRenderBarChart({
      selectedYear: 2024,
      selectedSystemBenchmarks: ['All Country', 'US Large'],
      selectedCustomBenchmarkIds: ['custom-1'],
      benchmarkReturns: {
        'All Country': 10.1,
        'US Large': 9.9,
      },
      benchmarkReturnYears: {
        'All Country': 2024,
        'US Large': 2024,
      },
      customBenchmarkReturns: {
        'custom-1': 7.7,
      },
      customBenchmarkReturnYears: {
        'custom-1': 2024,
      },
    });

    expect(result).toBe(true);
  });

  it('should switch from ready to not-ready when selectedYear changes but data year has not switched yet', () => {
    const readyFor2024 = shouldRenderBarChart({
      selectedYear: 2024,
      selectedSystemBenchmarks: ['All Country'],
      selectedCustomBenchmarkIds: [],
      benchmarkReturns: { 'All Country': 5.5 },
      benchmarkReturnYears: { 'All Country': 2024 },
      customBenchmarkReturns: {},
      customBenchmarkReturnYears: {},
    });

    const notReadyFor2025 = shouldRenderBarChart({
      selectedYear: 2025,
      selectedSystemBenchmarks: ['All Country'],
      selectedCustomBenchmarkIds: [],
      benchmarkReturns: { 'All Country': 5.5 },
      benchmarkReturnYears: { 'All Country': 2024 },
      customBenchmarkReturns: {},
      customBenchmarkReturnYears: {},
    });

    expect(readyFor2024).toBe(true);
    expect(notReadyFor2025).toBe(false);
  });
});
