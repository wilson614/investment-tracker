import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, within, waitFor } from '@testing-library/react';
import {
  StockMarket,
  type AvailableYears,
  type Portfolio,
  type YearPerformance,
  type StockQuoteResponse,
  type EuronextQuoteResponse,
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

vi.mock('../components/performance/YearSelector', () => ({
  YearSelector: () => <div data-testid="year-selector" />,
}));

vi.mock('../components/performance/CurrencyToggle', () => ({
  CurrencyToggle: () => <div data-testid="currency-toggle" />,
}));

vi.mock('../components/modals/MissingPriceModal', () => ({
  MissingPriceModal: () => null,
}));

vi.mock('../components/charts', () => ({
  PerformanceBarChart: ({ height }: { height?: number }) => (
    <div data-testid="performance-bar-chart" data-height={height ?? ''} />
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

const mockedUsePortfolio = vi.mocked(usePortfolio);
const mockedUseHistoricalPerformance = vi.mocked(useHistoricalPerformance);
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });
const mockedMarketDataApi = vi.mocked(marketDataApi, { deep: true });
const mockedUserBenchmarkApi = vi.mocked(userBenchmarkApi, { deep: true });
const mockedUserPreferencesApi = vi.mocked(userPreferencesApi, { deep: true });

const currentYear = new Date().getFullYear();

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
  years: [currentYear],
  earliestYear: currentYear,
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

function setupPageMocks(performance: YearPerformance): void {
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
    performance,
    isLoadingYears: false,
    isLoadingPerformance: false,
    error: null,
    setSelectedYear: vi.fn(),
    calculatePerformance: vi.fn(async () => undefined),
    refresh: vi.fn(async () => undefined),
  });
}

describe('PerformancePage metrics binding regression', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();

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
});
