import { describe, it, expect, beforeEach, vi } from 'vitest';
import { act, fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import {
  StockMarket,
  type AvailableYears,
  type Portfolio,
  type YearPerformance,
  type StockQuoteResponse,
  type EuronextQuoteResponse,
  type MarketYtdComparison,
} from '../types';
import type { UserPreferences } from '../services/api';
import { DEFAULT_BENCHMARKS } from '../constants';

vi.mock('react-router-dom', () => ({
  useNavigate: vi.fn(() => vi.fn()),
}));

vi.mock('../contexts/PortfolioContext', () => ({
  usePortfolio: vi.fn(),
}));

vi.mock('../hooks/useHistoricalPerformance', () => ({
  useHistoricalPerformance: vi.fn(),
}));

vi.mock('../components/portfolio/PortfolioSelector', () => ({
  PortfolioSelector: () => <div data-testid="portfolio-selector" />,
}));

const mockedYearSelector = vi.fn(
  ({ onChange }: { onChange: (year: number) => void }) => (
    <div data-testid="year-selector">
      <button
        type="button"
        data-testid="year-selector-prev"
        onClick={() => onChange(previousYear)}
      >
        prev-year
      </button>
      <button
        type="button"
        data-testid="year-selector-current"
        onClick={() => onChange(currentYear)}
      >
        current-year
      </button>
    </div>
  )
);

vi.mock('../components/performance/YearSelector', () => ({
  YearSelector: (props: { onChange: (year: number) => void }) => mockedYearSelector(props),
}));

vi.mock('../components/performance/CurrencyToggle', () => ({
  CurrencyToggle: () => <div data-testid="currency-toggle" />,
}));

vi.mock('../components/modals/MissingPriceModal', () => ({
  MissingPriceModal: () => null,
}));

vi.mock('../components/charts', () => ({
  PerformanceBarChart: ({
    data = [],
    height,
  }: {
    data?: Array<{ label: string; value: number; tooltip?: string }>;
    height?: number;
  }) => (
    <div data-testid="performance-bar-chart" data-height={height ?? ''}>
      {data.map((item) => (
        <div key={item.label} data-testid="performance-bar-item">
          {`${item.label}:${item.value}`}
        </div>
      ))}
    </div>
  ),
}));

vi.mock('../services/ytdApi', () => ({
  loadCachedYtdData: vi.fn(() => ({ data: null, isStale: true, needsMigration: false })),
  getYtdData: vi.fn(async () => ({
    year: new Date().getFullYear(),
    benchmarks: [],
    generatedAt: '2026-01-01T00:00:00.000Z',
  })),
  transformYtdData: vi.fn((data: unknown) => data),
}));

vi.mock('../services/api', () => ({
  stockPriceApi: {
    getQuote: vi.fn(),
    getQuoteWithRate: vi.fn(),
    getExchangeRateValue: vi.fn(),
  },
  marketDataApi: {
    getBenchmarkReturns: vi.fn(),
    getHistoricalPrices: vi.fn(),
    getEuronextQuoteByTicker: vi.fn(),
    getHistoricalExchangeRate: vi.fn(),
  },
  userBenchmarkApi: {
    getAll: vi.fn(),
  },
  userPreferencesApi: {
    get: vi.fn(),
    update: vi.fn(),
  },
}));

import { PerformancePage } from '../pages/Performance';
import { usePortfolio } from '../contexts/PortfolioContext';
import { useHistoricalPerformance } from '../hooks/useHistoricalPerformance';
import {
  stockPriceApi,
  marketDataApi,
  userBenchmarkApi,
  userPreferencesApi,
} from '../services/api';
import { loadCachedYtdData } from '../services/ytdApi';

const mockedUsePortfolio = vi.mocked(usePortfolio);
const mockedUseHistoricalPerformance = vi.mocked(useHistoricalPerformance);
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });
const mockedMarketDataApi = vi.mocked(marketDataApi, { deep: true });
const mockedUserBenchmarkApi = vi.mocked(userBenchmarkApi, { deep: true });
const mockedUserPreferencesApi = vi.mocked(userPreferencesApi, { deep: true });
const mockedLoadCachedYtdData = vi.mocked(loadCachedYtdData);

const currentYear = new Date().getFullYear();
const previousYear = currentYear - 1;

type BenchmarkReturnsResponse = {
  year: number;
  returns: Record<string, number | null>;
  hasStartPrices: boolean;
  hasEndPrices: boolean;
  dataSources: Record<string, string | null>;
};

const mockPortfolio: Portfolio = {
  id: 'portfolio-1',
  baseCurrency: 'USD',
  homeCurrency: 'TWD',
  isActive: true,
  displayName: 'USD Portfolio',
  boundCurrencyLedgerId: 'ledger-1',
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

const mockAvailableYears: AvailableYears = {
  years: [previousYear, currentYear],
  earliestYear: previousYear,
  currentYear,
};

const mockUserPreferences: UserPreferences = {
  ytdBenchmarkPreferences: null,
  capeRegionPreferences: null,
  defaultPortfolioId: null,
};

function createPerformanceMock(overrides: Partial<YearPerformance> = {}): YearPerformance {
  return {
    year: currentYear,
    xirr: null,
    xirrPercentage: null,
    totalReturnPercentage: 0,
    modifiedDietzPercentage: 12.34,
    timeWeightedReturnPercentage: -5.67,
    startValueHome: 1000,
    endValueHome: 1200,
    netContributionsHome: 100,
    sourceCurrency: 'USD',
    xirrSource: null,
    xirrPercentageSource: null,
    totalReturnPercentageSource: 0,
    modifiedDietzPercentageSource: 21.43,
    timeWeightedReturnPercentageSource: 3.21,
    startValueSource: 100,
    endValueSource: 120,
    netContributionsSource: 10,
    cashFlowCount: 2,
    transactionCount: 2,
    earliestTransactionDateInYear: `${currentYear}-01-01`,
    missingPrices: [],
    isComplete: true,
    ...overrides,
  };
}

function setupPageMocks(
  performance: YearPerformance,
  options?: {
    selectedYear?: number;
    setSelectedYear?: (year: number) => void;
    availableYears?: AvailableYears;
    calculatePerformance?: (...args: unknown[]) => Promise<void>;
  }
): void {
  mockedUsePortfolio.mockReturnValue({
    currentPortfolio: mockPortfolio,
    currentPortfolioId: mockPortfolio.id,
    isAllPortfolios: false,
    portfolios: [mockPortfolio],
    isLoading: false,
    selectPortfolio: vi.fn(),
    refreshPortfolios: vi.fn(async () => undefined),
    clearPerformanceState: vi.fn(),
    invalidateSharedCaches: vi.fn(),
    performanceVersion: 1,
  });

  mockedUseHistoricalPerformance.mockReturnValue({
    availableYears: options?.availableYears ?? mockAvailableYears,
    selectedYear: options?.selectedYear ?? currentYear,
    performance,
    isLoadingYears: false,
    isLoadingPerformance: false,
    error: null,
    setSelectedYear: options?.setSelectedYear ?? vi.fn(),
    calculatePerformance: options?.calculatePerformance ?? vi.fn(async () => undefined),
    refresh: vi.fn(async () => undefined),
  });
}

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error?: unknown) => void;

  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });

  return { promise, resolve, reject };
}

function buildBenchmarkReturns(startValue: number, step: number): Record<string, number> {
  const entries = DEFAULT_BENCHMARKS.map((key, index) => [
    key,
    Number((startValue - index * step).toFixed(2)),
  ] as const);

  return Object.fromEntries(entries) as Record<string, number>;
}

function buildBenchmarkResponse(year: number, returns: Record<string, number>): BenchmarkReturnsResponse {
  const dataSources = Object.fromEntries(
    Object.keys(returns).map((key) => [key, 'Yahoo'])
  ) as Record<string, string>;

  return {
    year,
    returns,
    hasStartPrices: true,
    hasEndPrices: true,
    dataSources,
  };
}

function expectPerformanceCardsLoadingGate(): void {
  const annualSummaryHeading = screen.getByText(new RegExp(`${currentYear} 年度報酬`));
  const valueSummaryHeading = screen.getByText(new RegExp(`${currentYear} 年度摘要`));

  const annualSummaryCard = annualSummaryHeading.closest('.card-dark');
  const valueSummaryCard = valueSummaryHeading.closest('.card-dark');

  expect(annualSummaryCard).not.toBeNull();
  expect(valueSummaryCard).not.toBeNull();

  expect(within(annualSummaryCard as HTMLElement).getByText('計算績效中...')).toBeInTheDocument();
  expect(within(valueSummaryCard as HTMLElement).getByText('計算績效中...')).toBeInTheDocument();

  expect(within(annualSummaryCard as HTMLElement).queryByText('—')).not.toBeInTheDocument();
  expect(within(annualSummaryCard as HTMLElement).queryByText('-', { exact: true })).not.toBeInTheDocument();
  expect(within(annualSummaryCard as HTMLElement).queryByText('+0.00%')).not.toBeInTheDocument();
  expect(within(annualSummaryCard as HTMLElement).queryByText('0.00%')).not.toBeInTheDocument();

  expect(within(valueSummaryCard as HTMLElement).queryByText('—')).not.toBeInTheDocument();
  expect(within(valueSummaryCard as HTMLElement).queryByText('-', { exact: true })).not.toBeInTheDocument();
  expect(within(valueSummaryCard as HTMLElement).queryByText(/\b0\s+TWD\b/)).not.toBeInTheDocument();
}

describe('PerformancePage metrics binding regression', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();

    mockedLoadCachedYtdData.mockReturnValue({
      data: null,
      isStale: true,
      needsMigration: false,
    });
    mockedYearSelector.mockClear();

    mockedUserBenchmarkApi.getAll.mockResolvedValue([]);
    mockedUserPreferencesApi.get.mockResolvedValue(mockUserPreferences);
    mockedUserPreferencesApi.update.mockResolvedValue(mockUserPreferences);

    mockedMarketDataApi.getBenchmarkReturns.mockResolvedValue({
      year: currentYear,
      returns: {},
      hasStartPrices: true,
      hasEndPrices: true,
      dataSources: {},
    });
    mockedMarketDataApi.getHistoricalPrices.mockResolvedValue({});
    const euronextQuoteMock: EuronextQuoteResponse = {
      price: 0,
      currency: 'EUR',
      marketTime: null,
      name: null,
      exchangeRate: 1,
      fromCache: true,
      change: null,
      changePercent: null,
    };
    mockedMarketDataApi.getEuronextQuoteByTicker.mockResolvedValue(euronextQuoteMock);
    mockedMarketDataApi.getHistoricalExchangeRate.mockResolvedValue({
      rate: 1,
      fromCurrency: 'USD',
      toCurrency: 'TWD',
      actualDate: `${currentYear}-12-31`,
    });

    const stockQuoteMock: StockQuoteResponse = {
      symbol: 'AAPL',
      name: 'Apple Inc.',
      price: 100,
      market: StockMarket.US,
      source: 'test',
      fetchedAt: '2026-01-01T00:00:00.000Z',
      exchangeRate: 1,
      exchangeRatePair: 'USD/TWD',
    };
    mockedStockPriceApi.getQuote.mockResolvedValue(stockQuoteMock);
    mockedStockPriceApi.getQuoteWithRate.mockResolvedValue(stockQuoteMock);
    mockedStockPriceApi.getExchangeRateValue.mockResolvedValue(1);
  });

  it('shows revised MD/TWR helper wording and removes deprecated copy', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    expect(await screen.findByText('衡量比例的重壓 (Modified Dietz)')).toBeInTheDocument();
    expect(screen.getByText('衡量本金的重壓 (TWR)')).toBeInTheDocument();
    expect(screen.queryByText('衡量投資人操作 (Modified Dietz)')).not.toBeInTheDocument();
    expect(screen.queryByText('衡量資產本身表現 (TWR)')).not.toBeInTheDocument();
  });

  it('uses home-currency fields for Modified Dietz and TWR cards', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    const modifiedDietzLabel = await screen.findByText('資金加權報酬率');
    const twrLabel = await screen.findByText('時間加權報酬率');

    const modifiedDietzCard = modifiedDietzLabel.closest('div')?.parentElement;
    const twrCard = twrLabel.closest('div')?.parentElement;

    expect(modifiedDietzCard).not.toBeNull();
    expect(twrCard).not.toBeNull();

    await waitFor(() => {
      expect(within(modifiedDietzCard as HTMLElement).getByText('+12.34%')).toBeInTheDocument();
      expect(within(twrCard as HTMLElement).getByText('-5.67%')).toBeInTheDocument();
    });

    expect(within(modifiedDietzCard as HTMLElement).queryByText('-5.67%')).not.toBeInTheDocument();
    expect(within(twrCard as HTMLElement).queryByText('+12.34%')).not.toBeInTheDocument();
  });

  it('uses source-currency fields for Modified Dietz and TWR cards', async () => {
    localStorage.setItem('performance_currency_mode', 'source');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    const modifiedDietzLabel = await screen.findByText('資金加權報酬率');
    const twrLabel = await screen.findByText('時間加權報酬率');

    const modifiedDietzCard = modifiedDietzLabel.closest('div')?.parentElement;
    const twrCard = twrLabel.closest('div')?.parentElement;

    expect(modifiedDietzCard).not.toBeNull();
    expect(twrCard).not.toBeNull();

    await waitFor(() => {
      expect(within(modifiedDietzCard as HTMLElement).getByText('+21.43%')).toBeInTheDocument();
      expect(within(twrCard as HTMLElement).getByText('+3.21%')).toBeInTheDocument();
    });

    expect(within(modifiedDietzCard as HTMLElement).queryByText('+3.21%')).not.toBeInTheDocument();
    expect(within(twrCard as HTMLElement).queryByText('+21.43%')).not.toBeInTheDocument();
  });

  it('does not show main loading gate or trigger recalculation when historical cache data is complete', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const calculatePerformanceMock = vi.fn(async () => undefined);
    setupPageMocks(
      createPerformanceMock({
        year: previousYear,
        isComplete: true,
        missingPrices: [],
      }),
      {
        selectedYear: previousYear,
        calculatePerformance: calculatePerformanceMock,
      },
    );

    render(<PerformancePage />);

    await waitFor(() => {
      expect(screen.getByText(new RegExp(`${previousYear} 年度報酬`))).toBeInTheDocument();
      expect(screen.queryByText('計算績效中...')).not.toBeInTheDocument();
    });

    expect(calculatePerformanceMock).not.toHaveBeenCalled();
  });

  it('shows loading gate and suppresses placeholders when performance is marked incomplete', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock({ isComplete: false }));

    render(<PerformancePage />);

    await waitFor(() => {
      expectPerformanceCardsLoadingGate();
    });
  });

  it('transitions from loading gate to bound values after rerender when key fields become ready', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    setupPageMocks(
      createPerformanceMock({
        isComplete: true,
        modifiedDietzPercentage: null,
        timeWeightedReturnPercentage: null,
        startValueHome: null,
        endValueHome: null,
      })
    );

    const { rerender } = render(<PerformancePage />);

    await waitFor(() => {
      expectPerformanceCardsLoadingGate();
    });

    setupPageMocks(createPerformanceMock());
    rerender(<PerformancePage />);

    const modifiedDietzLabel = await screen.findByText('資金加權報酬率');
    const twrLabel = await screen.findByText('時間加權報酬率');

    const modifiedDietzCard = modifiedDietzLabel.closest('div')?.parentElement;
    const twrCard = twrLabel.closest('div')?.parentElement;

    expect(modifiedDietzCard).not.toBeNull();
    expect(twrCard).not.toBeNull();

    await waitFor(() => {
      expect(within(modifiedDietzCard as HTMLElement).getByText('+12.34%')).toBeInTheDocument();
      expect(within(twrCard as HTMLElement).getByText('-5.67%')).toBeInTheDocument();
    });

    expect(screen.queryByText('計算績效中...')).not.toBeInTheDocument();
  });

  it('renders portfolio selector and currency toggle in the same control row', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    const controlRow = await screen.findByTestId('performance-control-row');
    const portfolioSelector = screen.getByTestId('portfolio-selector');
    const currencyToggle = screen.getByTestId('currency-toggle');

    expect(controlRow).toContainElement(portfolioSelector);
    expect(controlRow).toContainElement(currencyToggle);
  });

  it('keeps benchmark chart height stable by selected benchmark count', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    const chart = await screen.findByTestId('performance-bar-chart');
    const expectedHeight = 80 + (DEFAULT_BENCHMARKS.length + 1) * 40;

    expect(chart).toHaveAttribute('data-height', expectedHeight.toString());
  });

  it('does not render historical-year benchmark bars with current-year cache before selected year data is ready', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const historicalDeferred = createDeferred<BenchmarkReturnsResponse>();
    mockedMarketDataApi.getBenchmarkReturns.mockImplementation(async (year: number) => {
      if (year === previousYear) {
        return historicalDeferred.promise;
      }

      return buildBenchmarkResponse(year, {});
    });

    const cachedCurrentYearYtd: MarketYtdComparison = {
      year: currentYear,
      generatedAt: '2026-01-01T00:00:00.000Z',
      benchmarks: DEFAULT_BENCHMARKS.map((key, index) => ({
        marketKey: key,
        symbol: key,
        name: key,
        jan1Price: 100,
        currentPrice: 110,
        ytdReturnPercent: 20 + index,
        fetchedAt: '2026-01-01T00:00:00.000Z',
        error: null,
      })),
    };

    mockedLoadCachedYtdData.mockReturnValue({
      data: cachedCurrentYearYtd,
      isStale: false,
      needsMigration: false,
    });

    setupPageMocks(
      createPerformanceMock({ year: previousYear }),
      { selectedYear: previousYear }
    );

    render(<PerformancePage />);

    expect(await screen.findByText('載入基準報酬中...')).toBeInTheDocument();
    expect(screen.queryByTestId('performance-bar-chart')).not.toBeInTheDocument();

    historicalDeferred.resolve(
      buildBenchmarkResponse(previousYear, buildBenchmarkReturns(7.77, 1.11))
    );

    await waitFor(() => {
      expect(screen.getByTestId('performance-bar-chart')).toBeInTheDocument();
      expect(screen.getByText('全球 (VWRA):7.77')).toBeInTheDocument();
      expect(screen.queryByText('全球 (VWRA):20')).not.toBeInTheDocument();
    });
  });

  it('ignores stale benchmark response when switching years quickly', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const historicalYearsOnly: AvailableYears = {
      years: [previousYear, currentYear],
      earliestYear: previousYear,
      currentYear: currentYear + 1,
    };

    const previousYearDeferred = createDeferred<BenchmarkReturnsResponse>();
    const currentYearDeferred = createDeferred<BenchmarkReturnsResponse>();

    mockedMarketDataApi.getBenchmarkReturns.mockImplementation(async (year: number) => {
      if (year === previousYear) {
        return previousYearDeferred.promise;
      }

      if (year === currentYear) {
        return currentYearDeferred.promise;
      }

      return buildBenchmarkResponse(year, {});
    });

    let selectedYearState = previousYear;
    const setSelectedYearMock = vi.fn((year: number) => {
      selectedYearState = year;
    });

    const rerenderWithYear = () => {
      setupPageMocks(
        createPerformanceMock({ year: selectedYearState }),
        {
          selectedYear: selectedYearState,
          availableYears: historicalYearsOnly,
          setSelectedYear: setSelectedYearMock,
        }
      );
    };

    rerenderWithYear();
    const { rerender } = render(<PerformancePage />);

    expect(await screen.findByText('載入基準報酬中...')).toBeInTheDocument();

    const yearSelectorProps = mockedYearSelector.mock.lastCall?.[0];
    expect(yearSelectorProps).toBeDefined();

    await act(async () => {
      fireEvent.click(screen.getByTestId('year-selector-current'));
      rerenderWithYear();
      rerender(<PerformancePage />);
    });

    await waitFor(() => {
      expect(setSelectedYearMock).toHaveBeenCalledWith(currentYear);
      expect(mockedMarketDataApi.getBenchmarkReturns).toHaveBeenCalledWith(currentYear);
    });

    currentYearDeferred.resolve(
      buildBenchmarkResponse(currentYear, buildBenchmarkReturns(30, 1.5))
    );

    await waitFor(() => {
      expect(screen.getByTestId('performance-bar-chart')).toBeInTheDocument();
      expect(screen.getByText('全球 (VWRA):30')).toBeInTheDocument();
    });

    previousYearDeferred.resolve(
      buildBenchmarkResponse(previousYear, buildBenchmarkReturns(9.99, 1.11))
    );

    await waitFor(() => {
      expect(screen.getByText('全球 (VWRA):30')).toBeInTheDocument();
      expect(screen.queryByText('全球 (VWRA):9.99')).not.toBeInTheDocument();
    });
  });
});
