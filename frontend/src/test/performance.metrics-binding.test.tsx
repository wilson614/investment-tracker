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
  type MissingPrice,
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
  CurrencyToggle: ({
    onChange,
  }: {
    onChange: (mode: 'source' | 'home') => void;
  }) => (
    <div data-testid="currency-toggle">
      <button
        type="button"
        data-testid="currency-toggle-home"
        onClick={() => onChange('home')}
      >
        home
      </button>
      <button
        type="button"
        data-testid="currency-toggle-source"
        onClick={() => onChange('source')}
      >
        source
      </button>
    </div>
  ),
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
import { getYtdData, loadCachedYtdData } from '../services/ytdApi';

const mockedUsePortfolio = vi.mocked(usePortfolio);
const mockedUseHistoricalPerformance = vi.mocked(useHistoricalPerformance);
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });
const mockedMarketDataApi = vi.mocked(marketDataApi, { deep: true });
const mockedUserBenchmarkApi = vi.mocked(userBenchmarkApi, { deep: true });
const mockedUserPreferencesApi = vi.mocked(userPreferencesApi, { deep: true });
const mockedLoadCachedYtdData = vi.mocked(loadCachedYtdData);
const mockedGetYtdData = vi.mocked(getYtdData);

const currentYear = new Date().getFullYear();
const previousYear = currentYear - 1;

type BenchmarkReturnsResponse = {
  year: number;
  returns: Record<string, number | null>;
  hasStartPrices: boolean;
  hasEndPrices: boolean;
  dataSources: Record<string, string | null>;
};

type HistoricalPricesResponse = Record<string, {
  price: number;
  currency: string;
  actualDate: string;
}>;

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
    coverageStartDate: `${currentYear}-01-01`,
    coverageDays: 365,
    hasOpeningBaseline: false,
    usesPartialHistoryAssumption: false,
    xirrReliability: 'High',
    shouldDegradeReturnDisplay: false,
    returnDisplayDegradeReasonCode: null,
    returnDisplayDegradeReasonMessage: null,
    hasRecentLargeInflowWarning: false,
    missingPrices: [],
    isComplete: true,
    ...overrides,
  };
}

function createMissingPrice(
  ticker: string,
  overrides: Partial<MissingPrice> = {}
): MissingPrice {
  return {
    ticker,
    date: `${currentYear}-12-31`,
    priceType: 'YearEnd',
    market: StockMarket.US,
    ...overrides,
  };
}

function seedQuoteCache(key: string, price: number, exchangeRate: number): void {
  localStorage.setItem(
    key,
    JSON.stringify({
      quote: { price, exchangeRate },
      updatedAt: '2026-01-01T00:00:00.000Z',
    }),
  );
}

function setupPageMocks(
  performance: YearPerformance,
  options?: {
    selectedYear?: number;
    setSelectedYear?: (year: number) => void;
    availableYears?: AvailableYears;
    calculatePerformance?: (...args: unknown[]) => Promise<void>;
    isAllPortfolios?: boolean;
  }
): void {
  const isAllPortfolios = options?.isAllPortfolios ?? false;

  mockedUsePortfolio.mockReturnValue({
    currentPortfolio: isAllPortfolios ? null : mockPortfolio,
    currentPortfolioId: isAllPortfolios ? 'all' : mockPortfolio.id,
    isAllPortfolios,
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

    mockedGetYtdData.mockReset();
    mockedLoadCachedYtdData.mockReset();
    mockedUserBenchmarkApi.getAll.mockReset();
    mockedUserPreferencesApi.get.mockReset();
    mockedUserPreferencesApi.update.mockReset();
    mockedMarketDataApi.getBenchmarkReturns.mockReset();
    mockedMarketDataApi.getHistoricalPrices.mockReset();
    mockedMarketDataApi.getEuronextQuoteByTicker.mockReset();
    mockedMarketDataApi.getHistoricalExchangeRate.mockReset();
    mockedStockPriceApi.getQuote.mockReset();
    mockedStockPriceApi.getQuoteWithRate.mockReset();
    mockedStockPriceApi.getExchangeRateValue.mockReset();

    mockedGetYtdData.mockResolvedValue({
      year: currentYear,
      benchmarks: [],
      generatedAt: '2026-01-01T00:00:00.000Z',
    });

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

  it('shows revised MD/TWR helper wording with tooltip a11y semantics and removes deprecated copy', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(createPerformanceMock());

    render(<PerformancePage />);

    expect(await screen.findByText('衡量比例的重壓 (Modified Dietz)')).toBeInTheDocument();
    expect(screen.getByText('衡量本金的重壓 (TWR)')).toBeInTheDocument();
    expect(screen.queryByText('衡量投資人操作 (Modified Dietz)')).not.toBeInTheDocument();
    expect(screen.queryByText('衡量資產本身表現 (TWR)')).not.toBeInTheDocument();

    const annualInfoButton = screen.getByRole('button', { name: '年度報酬說明' });
    const mdInfoButton = screen.getByRole('button', { name: '資金加權報酬率說明' });
    const twrInfoButton = screen.getByRole('button', { name: '時間加權報酬率說明' });

    expect(annualInfoButton).toHaveAttribute('aria-describedby', 'annual-return-tooltip');
    expect(mdInfoButton).toHaveAttribute('aria-describedby', 'md-tooltip');
    expect(twrInfoButton).toHaveAttribute('aria-describedby', 'twr-tooltip');

    const annualTooltip = document.getElementById('annual-return-tooltip');
    const mdTooltip = document.getElementById('md-tooltip');
    const twrTooltip = document.getElementById('twr-tooltip');

    expect(annualTooltip).toHaveAttribute('role', 'tooltip');
    expect(mdTooltip).toHaveAttribute('role', 'tooltip');
    expect(twrTooltip).toHaveAttribute('role', 'tooltip');

    expect(annualTooltip).toHaveTextContent('2 筆交易');
    expect(mdTooltip).toHaveTextContent('衡量比例的重壓 (Modified Dietz)');
    expect(twrTooltip).toHaveTextContent('衡量本金的重壓 (TWR)');

    expect(annualTooltip?.parentElement).toHaveClass('group-focus-within:block');
    expect(mdTooltip?.parentElement).toHaveClass('group-focus-within:block');
    expect(twrTooltip?.parentElement).toHaveClass('group-focus-within:block');
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

  it('shows coverage signals for partial-history yearly data and does not render XIRR card', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        coverageStartDate: `${currentYear}-11-01`,
        coverageDays: 45,
        hasOpeningBaseline: true,
        usesPartialHistoryAssumption: true,
        xirrReliability: 'Unavailable',
      })
    );

    render(<PerformancePage />);

    expect(await screen.findByText(/資料覆蓋有限/)).toBeInTheDocument();
    expect(screen.getByText(/此年度含節錄匯入假設/)).toBeInTheDocument();
    expect(screen.getByText(/已套用期初基準/)).toBeInTheDocument();
    expect(screen.queryByText('年化報酬率 (XIRR)')).not.toBeInTheDocument();
  });

  it('shows degrade hint and keeps MD/TWR visible when return display should degrade', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        shouldDegradeReturnDisplay: true,
        returnDisplayDegradeReasonCode: 'LOW_CONFIDENCE_LOW_COVERAGE',
      })
    );

    render(<PerformancePage />);

    expect(await screen.findByText(/低信度年度：此年度資料覆蓋天數不足，資金加權報酬率（MD）信度偏低。/)).toBeInTheDocument();
    expect(screen.queryByText(/已停用單年度年化報酬（XIRR）主顯示/)).not.toBeInTheDocument();
    expect(screen.getByText('資金加權報酬率')).toBeInTheDocument();
    expect(screen.getByText('時間加權報酬率')).toBeInTheDocument();
    expect(screen.queryByText('年化報酬率 (XIRR)')).not.toBeInTheDocument();
  });

  it('shows fallback degrade hint from backend reason message when reason code is unknown', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        shouldDegradeReturnDisplay: true,
        returnDisplayDegradeReasonCode: 'SOME_UNKNOWN_CODE',
        returnDisplayDegradeReasonMessage: 'Low confidence aggregate performance: insufficient coverage period.',
      })
    );

    render(<PerformancePage />);

    expect(
      await screen.findByText(/此年度資金加權報酬率（MD）信度偏低（Low confidence aggregate performance: insufficient coverage period\.）/)
    ).toBeInTheDocument();
    expect(screen.queryByText('年化報酬率 (XIRR)')).not.toBeInTheDocument();
  });

  it('shows recent large inflow warning when backend signal is true', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        hasRecentLargeInflowWarning: true,
      })
    );

    render(<PerformancePage />);

    expect(
      await screen.findByText('近期大額資金異動可能導致資金加權報酬率短期波動。')
    ).toBeInTheDocument();
  });

  it('keeps XIRR card hidden while performance values are still incomplete', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        isComplete: false,
      })
    );

    render(<PerformancePage />);

    await waitFor(() => {
      expect(screen.getAllByText('計算績效中...').length).toBeGreaterThan(0);
    });
    expect(screen.queryByText('年化報酬率 (XIRR)')).not.toBeInTheDocument();
  });

  it('binds total return to home/source fields when currency mode changes', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    setupPageMocks(
      createPerformanceMock({
        totalReturnPercentage: 12.34,
        totalReturnPercentageSource: 56.78,
      })
    );

    render(<PerformancePage />);

    const totalReturnLabel = await screen.findByText('總報酬率');
    const totalReturnCell = totalReturnLabel.parentElement;
    expect(totalReturnCell).not.toBeNull();

    await waitFor(() => {
      expect(within(totalReturnCell as HTMLElement).getByText('+12.34%')).toBeInTheDocument();
      expect(within(totalReturnCell as HTMLElement).queryByText('+56.78%')).not.toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('currency-toggle-source'));

    await waitFor(() => {
      expect(within(totalReturnCell as HTMLElement).getByText('+56.78%')).toBeInTheDocument();
      expect(within(totalReturnCell as HTMLElement).queryByText('+12.34%')).not.toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('currency-toggle-home'));

    await waitFor(() => {
      expect(within(totalReturnCell as HTMLElement).getByText('+12.34%')).toBeInTheDocument();
      expect(within(totalReturnCell as HTMLElement).queryByText('+56.78%')).not.toBeInTheDocument();
    });
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

    expect(screen.getAllByText(/目前階段：準備資料。正在整理/).length).toBeGreaterThan(0);
  });

  it('shows staged loading feedback and long-wait reminder for performance calculation', async () => {
    vi.useFakeTimers();

    try {
      localStorage.setItem('performance_currency_mode', 'home');

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
        availableYears: mockAvailableYears,
        selectedYear: currentYear,
        performance: null,
        isLoadingYears: false,
        isLoadingPerformance: true,
        error: null,
        setSelectedYear: vi.fn(),
        calculatePerformance: vi.fn(async () => undefined),
        refresh: vi.fn(async () => undefined),
      });

      render(<PerformancePage />);

      expect(screen.getByText('計算績效中...')).toBeInTheDocument();
      expect(screen.getByText('準備資料 → 補齊價格 → 計算中 → 較久等待提醒')).toBeInTheDocument();
      expect(screen.getByText('目前階段：準備資料')).toBeInTheDocument();

      await act(async () => {
        vi.advanceTimersByTime(3000);
      });

      expect(screen.getByText('目前階段：計算中')).toBeInTheDocument();
      expect(screen.getByText('正在重算報酬率與年度摘要，完成後會自動更新畫面。')).toBeInTheDocument();

      await act(async () => {
        vi.advanceTimersByTime(13000);
      });

      expect(screen.getByText('目前階段：較久等待提醒')).toBeInTheDocument();
      expect(screen.getByText('這次等待時間較長，可能是資料量較多或外部價格來源較慢，請稍候。')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
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

  it('falls back to benchmark returns API and unblocks loading when current-year YTD fetch is unavailable', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const fallbackReturns = buildBenchmarkReturns(9.01, 0.5);
    mockedLoadCachedYtdData.mockReturnValue({
      data: null,
      isStale: true,
      needsMigration: false,
    });
    mockedGetYtdData.mockResolvedValueOnce(null as any);
    mockedMarketDataApi.getBenchmarkReturns.mockResolvedValueOnce(
      buildBenchmarkResponse(currentYear, fallbackReturns)
    );

    setupPageMocks(createPerformanceMock(), { selectedYear: currentYear });

    render(<PerformancePage />);

    expect(await screen.findByText('載入基準報酬中...')).toBeInTheDocument();

    await waitFor(() => {
      expect(mockedGetYtdData).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(mockedMarketDataApi.getBenchmarkReturns).toHaveBeenCalledWith(currentYear);
    });

    await waitFor(() => {
      expect(screen.queryByText('載入基準報酬中...')).not.toBeInTheDocument();
      expect(screen.getByTestId('performance-bar-chart')).toBeInTheDocument();
      expect(screen.getByText(`全球 (VWRA):${fallbackReturns['All Country']}`)).toBeInTheDocument();
    });
  });

  it('deduplicates repeated missing tickers and avoids duplicate price fetches after performance refresh rerender', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const duplicateMissingPrices: MissingPrice[] = [
      createMissingPrice('AAPL'),
      createMissingPrice('AAPL'),
    ];

    const calculatePerformanceMock = vi.fn(async () => undefined);

    setupPageMocks(
      createPerformanceMock({
        missingPrices: duplicateMissingPrices,
      }),
      {
        selectedYear: currentYear,
        calculatePerformance: calculatePerformanceMock,
      },
    );

    const { rerender } = render(<PerformancePage />);

    await waitFor(() => {
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledTimes(1);
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledWith(
        StockMarket.US,
        'AAPL',
        mockPortfolio.homeCurrency,
      );
    });

    await waitFor(() => {
      expect(calculatePerformanceMock).toHaveBeenCalledTimes(1);
      expect(screen.queryByRole('button', { name: '重新抓取' })).not.toBeInTheDocument();
    });

    setupPageMocks(
      createPerformanceMock({
        missingPrices: [...duplicateMissingPrices],
      }),
      {
        selectedYear: currentYear,
        calculatePerformance: calculatePerformanceMock,
      },
    );

    rerender(<PerformancePage />);

    await waitFor(() => {
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledTimes(1);
    });
  });

  it('keeps YearStart and YearEnd missing entries for the same ticker in aggregate mode', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const aggregateMissingPrices: MissingPrice[] = [
      createMissingPrice('AAPL', {
        market: StockMarket.US,
        priceType: 'YearStart',
        date: `${previousYear}-12-31`,
      }),
      createMissingPrice('AAPL', {
        market: StockMarket.US,
        priceType: 'YearEnd',
        date: `${currentYear}-12-31`,
      }),
    ];

    setupPageMocks(
      createPerformanceMock({
        missingPrices: aggregateMissingPrices,
      }),
      {
        selectedYear: currentYear,
        isAllPortfolios: true,
      },
    );

    render(<PerformancePage />);

    await waitFor(() => {
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledTimes(1);
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledWith(
        StockMarket.US,
        'AAPL',
        'TWD',
      );
    });

    expect(screen.getByRole('button', { name: '重新抓取' })).toBeInTheDocument();
  });

  it('uses market-aware quote cache key first for current-year missing prices', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    seedQuoteCache('quote_cache_AAPL_2', 123, 31);

    const calculatePerformanceMock = vi.fn(async () => undefined);
    setupPageMocks(
      createPerformanceMock({
        missingPrices: [createMissingPrice('AAPL', { market: StockMarket.US })],
      }),
      {
        selectedYear: currentYear,
        calculatePerformance: calculatePerformanceMock,
      },
    );

    render(<PerformancePage />);

    await waitFor(() => {
      expect(calculatePerformanceMock).toHaveBeenCalledWith(
        currentYear,
        expect.objectContaining({
          AAPL: { price: 123, exchangeRate: 31 },
        }),
      );
    });

    expect(mockedStockPriceApi.getQuoteWithRate).not.toHaveBeenCalled();
  });

  it('falls back to legacy quote cache key when market-aware key is unavailable', async () => {
    localStorage.setItem('performance_currency_mode', 'home');
    seedQuoteCache('quote_cache_AAPL', 111, 29);

    const calculatePerformanceMock = vi.fn(async () => undefined);
    setupPageMocks(
      createPerformanceMock({
        missingPrices: [createMissingPrice('AAPL', { market: StockMarket.US })],
      }),
      {
        selectedYear: currentYear,
        calculatePerformance: calculatePerformanceMock,
      },
    );

    render(<PerformancePage />);

    await waitFor(() => {
      expect(calculatePerformanceMock).toHaveBeenCalledWith(
        currentYear,
        expect.objectContaining({
          AAPL: { price: 111, exchangeRate: 29 },
        }),
      );
    });

    expect(mockedStockPriceApi.getQuoteWithRate).not.toHaveBeenCalled();
  });

  it('ignores stale auto price-fetch response when switching years quickly', async () => {
    localStorage.setItem('performance_currency_mode', 'home');

    const historicalYearsOnly: AvailableYears = {
      years: [previousYear, currentYear],
      earliestYear: previousYear,
      currentYear: currentYear + 1,
    };

    const previousYearDeferred = createDeferred<HistoricalPricesResponse>();
    const currentYearDeferred = createDeferred<HistoricalPricesResponse>();

    mockedMarketDataApi.getHistoricalPrices.mockImplementation(async (_tickers, date) => {
      if (date === `${previousYear}-12-31`) {
        return previousYearDeferred.promise;
      }

      if (date === `${currentYear}-12-31`) {
        return currentYearDeferred.promise;
      }

      return {};
    });

    let selectedYearState = previousYear;
    const setSelectedYearMock = vi.fn((year: number) => {
      selectedYearState = year;
    });

    const calculatePerformanceMock = vi.fn(async () => undefined);

    const rerenderWithYear = () => {
      setupPageMocks(
        createPerformanceMock({
          year: selectedYearState,
          missingPrices: [createMissingPrice('AAPL', { market: StockMarket.US })],
        }),
        {
          selectedYear: selectedYearState,
          availableYears: historicalYearsOnly,
          setSelectedYear: setSelectedYearMock,
          calculatePerformance: calculatePerformanceMock,
        }
      );
    };

    rerenderWithYear();
    const { rerender } = render(<PerformancePage />);

    await waitFor(() => {
      expect(mockedMarketDataApi.getHistoricalPrices).toHaveBeenCalledWith(
        ['AAPL'],
        `${previousYear}-12-31`,
        { AAPL: StockMarket.US },
      );
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('year-selector-current'));
      rerenderWithYear();
      rerender(<PerformancePage />);
    });

    await waitFor(() => {
      expect(setSelectedYearMock).toHaveBeenCalledWith(currentYear);
      expect(mockedMarketDataApi.getHistoricalPrices).toHaveBeenCalledWith(
        ['AAPL'],
        `${currentYear}-12-31`,
        { AAPL: StockMarket.US },
      );
    });

    previousYearDeferred.resolve({});

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.queryByRole('button', { name: '重新抓取' })).not.toBeInTheDocument();

    currentYearDeferred.resolve({
      AAPL: {
        price: 321,
        currency: 'USD',
        actualDate: `${currentYear}-12-31`,
      },
    });

    await waitFor(() => {
      expect(calculatePerformanceMock).toHaveBeenCalledWith(
        currentYear,
        expect.objectContaining({
          AAPL: {
            price: 321,
            exchangeRate: 1,
          },
        }),
        expect.any(Object),
      );
    });

    expect(calculatePerformanceMock).not.toHaveBeenCalledWith(
      previousYear,
      expect.anything(),
      expect.anything(),
    );
    expect(screen.queryByRole('button', { name: '重新抓取' })).not.toBeInTheDocument();
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
