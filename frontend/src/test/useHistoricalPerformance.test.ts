import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import { portfolioApi } from '../services/api';
import type { AvailableYears, YearPerformance } from '../types';

vi.mock('../services/api', () => ({
  portfolioApi: {
    getAvailableYears: vi.fn(),
    getAggregateYears: vi.fn(),
    calculateYearPerformance: vi.fn(),
    calculateAggregateYearPerformance: vi.fn(),
  },
}));

const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });

const currentYear = new Date().getFullYear();
const previousYear = currentYear - 1;
const twoYearsAgo = currentYear - 2;

const aggregateYearsMock: AvailableYears = {
  years: [currentYear],
  earliestYear: currentYear,
  currentYear,
};

const singlePortfolioYearsMock: AvailableYears = {
  years: [currentYear, previousYear],
  earliestYear: previousYear,
  currentYear,
};

const aggregateEmptyYearsMock: AvailableYears = {
  years: [],
  earliestYear: null,
  currentYear,
};

const aggregatePerformanceMock: YearPerformance = {
  year: currentYear,
  xirr: null,
  xirrPercentage: null,
  totalReturnPercentage: 10,
  modifiedDietzPercentage: 10,
  timeWeightedReturnPercentage: 10,
  startValueHome: 100,
  endValueHome: 110,
  netContributionsHome: 0,
  sourceCurrency: 'TWD',
  xirrSource: null,
  xirrPercentageSource: null,
  totalReturnPercentageSource: 10,
  modifiedDietzPercentageSource: 10,
  timeWeightedReturnPercentageSource: 10,
  startValueSource: 100,
  endValueSource: 110,
  netContributionsSource: 0,
  cashFlowCount: 0,
  transactionCount: 0,
  earliestTransactionDateInYear: null,
  missingPrices: [],
  isComplete: true,
};

describe('useHistoricalPerformance', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();

    mockedPortfolioApi.getAggregateYears.mockResolvedValue(aggregateYearsMock);
    mockedPortfolioApi.getAvailableYears.mockResolvedValue(singlePortfolioYearsMock);
    mockedPortfolioApi.calculateAggregateYearPerformance.mockResolvedValue(aggregatePerformanceMock);
    mockedPortfolioApi.calculateYearPerformance.mockResolvedValue(aggregatePerformanceMock);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls aggregate endpoints when isAggregate is true', async () => {
    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(mockedPortfolioApi.calculateAggregateYearPerformance).toHaveBeenCalledTimes(1);
    });

    expect(mockedPortfolioApi.getAvailableYears).not.toHaveBeenCalled();
    expect(mockedPortfolioApi.calculateYearPerformance).not.toHaveBeenCalled();
    expect(result.current.error).toBeNull();
    expect(result.current.performance?.year).toBe(currentYear);
  });

  it('does not hydrate stale selectedYear/performance from cache when years are empty', async () => {
    localStorage.setItem(
      'perf_years_aggregate',
      JSON.stringify(aggregateEmptyYearsMock)
    );
    localStorage.setItem(
      `perf_data_aggregate_${currentYear}`,
      JSON.stringify({
        ...aggregatePerformanceMock,
        startValueSource: null,
      } satisfies YearPerformance)
    );

    mockedPortfolioApi.getAggregateYears.mockResolvedValueOnce(aggregateEmptyYearsMock);

    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(result.current.isLoadingYears).toBe(false);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.availableYears).toEqual(aggregateEmptyYearsMock);
    expect(result.current.selectedYear).toBeNull();
    expect(result.current.performance).toBeNull();
    expect(mockedPortfolioApi.calculateAggregateYearPerformance).not.toHaveBeenCalled();
  });

  it('clears stale aggregate selectedYear/performance after fetching empty years response', async () => {
    const staleYear = currentYear - 1;
    localStorage.setItem(
      'perf_years_aggregate',
      JSON.stringify({
        years: [staleYear],
        earliestYear: staleYear,
        currentYear: staleYear,
      } satisfies AvailableYears)
    );
    localStorage.setItem(
      `perf_data_aggregate_${staleYear}`,
      JSON.stringify({
        ...aggregatePerformanceMock,
        year: staleYear,
      } satisfies YearPerformance)
    );

    mockedPortfolioApi.getAggregateYears.mockResolvedValueOnce(aggregateEmptyYearsMock);

    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(result.current.isLoadingYears).toBe(false);
    });

    expect(result.current.availableYears).toEqual(aggregateEmptyYearsMock);
    expect(result.current.selectedYear).toBeNull();
    expect(result.current.performance).toBeNull();
    expect(mockedPortfolioApi.calculateAggregateYearPerformance).not.toHaveBeenCalled();
  });

  it('keeps selected year when switching specific portfolio to aggregate if year exists', async () => {
    const selectedHistoryYear = previousYear;

    mockedPortfolioApi.getAvailableYears.mockResolvedValueOnce({
      years: [currentYear, selectedHistoryYear],
      earliestYear: selectedHistoryYear,
      currentYear,
    });

    const { result, unmount } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'portfolio-a',
        isAggregate: false,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(result.current.selectedYear).toBe(currentYear);
    });

    act(() => {
      result.current.setSelectedYear(selectedHistoryYear);
    });

    expect(result.current.selectedYear).toBe(selectedHistoryYear);
    expect(localStorage.getItem('perf_selected_year')).toBe(String(selectedHistoryYear));

    unmount();

    mockedPortfolioApi.getAggregateYears.mockResolvedValueOnce({
      years: [currentYear, selectedHistoryYear],
      earliestYear: selectedHistoryYear,
      currentYear,
    });

    const { result: aggregateResult } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(aggregateResult.current.selectedYear).toBe(selectedHistoryYear);
    });
  });

  it('keeps selected year when switching aggregate to specific portfolio if year exists', async () => {
    const selectedHistoryYear = previousYear;

    mockedPortfolioApi.getAggregateYears.mockResolvedValueOnce({
      years: [currentYear, selectedHistoryYear],
      earliestYear: selectedHistoryYear,
      currentYear,
    });

    const { result, unmount } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    act(() => {
      result.current.setSelectedYear(selectedHistoryYear);
    });

    expect(result.current.selectedYear).toBe(selectedHistoryYear);

    unmount();

    mockedPortfolioApi.getAvailableYears.mockResolvedValueOnce({
      years: [currentYear, selectedHistoryYear],
      earliestYear: selectedHistoryYear,
      currentYear,
    });

    const { result: specificResult } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'portfolio-a',
        isAggregate: false,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAvailableYears).toHaveBeenCalledWith('portfolio-a');
    });

    await waitFor(() => {
      expect(specificResult.current.selectedYear).toBe(selectedHistoryYear);
    });
  });

  it('falls back to latest available year only when selected year does not exist after switch', async () => {
    const selectedHistoryYear = twoYearsAgo;

    mockedPortfolioApi.getAvailableYears.mockResolvedValueOnce({
      years: [currentYear, selectedHistoryYear],
      earliestYear: selectedHistoryYear,
      currentYear,
    });

    const { result, unmount } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'portfolio-a',
        isAggregate: false,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAvailableYears).toHaveBeenCalledWith('portfolio-a');
    });

    act(() => {
      result.current.setSelectedYear(selectedHistoryYear);
    });

    expect(result.current.selectedYear).toBe(selectedHistoryYear);

    unmount();

    mockedPortfolioApi.getAggregateYears.mockResolvedValueOnce({
      years: [currentYear, previousYear],
      earliestYear: previousYear,
      currentYear,
    });

    const { result: aggregateResult } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getAggregateYears).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(aggregateResult.current.selectedYear).toBe(currentYear);
    });
  });

  it('treats recognizable aggregate calculate 404 as empty state without error', async () => {
    const notFoundError = new Error('Portfolio not found') as Error & { status: number };
    notFoundError.status = 404;
    mockedPortfolioApi.calculateAggregateYearPerformance.mockRejectedValueOnce(notFoundError);

    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.calculateAggregateYearPerformance).toHaveBeenCalledTimes(1);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.performance).toBeNull();
  });

  it('does not swallow generic 404 from aggregate calculate endpoint', async () => {
    const genericNotFoundError = new Error('Transaction not found') as Error & { status: number };
    genericNotFoundError.status = 404;
    mockedPortfolioApi.calculateAggregateYearPerformance.mockRejectedValueOnce(genericNotFoundError);

    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.calculateAggregateYearPerformance).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(result.current.error).toBe('Transaction not found');
    });
  });

  it('keeps non-404 aggregate errors visible', async () => {
    const serverError = new Error('Server Error') as Error & { status: number };
    serverError.status = 500;
    mockedPortfolioApi.getAggregateYears.mockRejectedValueOnce(serverError);

    const { result } = renderHook(() =>
      useHistoricalPerformance({
        portfolioId: 'aggregate',
        isAggregate: true,
        autoFetch: true,
      })
    );

    await waitFor(() => {
      expect(result.current.error).toBe('Server Error');
    });

    expect(result.current.availableYears).toEqual({
      years: [],
      earliestYear: null,
      currentYear,
    });
  });
});
