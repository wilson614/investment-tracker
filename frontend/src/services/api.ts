import type {
  Portfolio,
  CreatePortfolioRequest,
  UpdatePortfolioRequest,
  StockTransaction,
  CreateStockTransactionRequest,
  UpdateStockTransactionRequest,
  PortfolioSummary,
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  CurrencyLedgerSummary,
  CurrencyLedger,
  CurrencyTransaction,
  CreateCurrencyLedgerRequest,
  UpdateCurrencyLedgerRequest,
  CreateCurrencyTransactionRequest,
  UpdateCurrencyTransactionRequest,
  CalculateXirrRequest,
  XirrResult,
  StockMarket,
  StockQuoteResponse,
  ExchangeRateResponse,
  MarketInfo,
  CapeData,
  MarketYtdComparison,
  EuronextQuoteResponse,
  AvailableYears,
  YearPerformance,
  CalculateYearPerformanceRequest,
  EtfClassificationResult,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_URL || '/api';

interface ApiErrorType extends Error {
  status: number;
}

function createApiError(status: number, message: string): ApiErrorType {
  const error = new Error(message) as ApiErrorType;
  error.name = 'ApiError';
  error.status = status;
  return error;
}

async function fetchApi<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const token = localStorage.getItem('token');

  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token && { Authorization: `Bearer ${token}` }),
      ...options.headers,
    },
  });

  if (!response.ok) {
    // Handle 401 Unauthorized - token expired or invalid
    if (response.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('user');
      // Redirect to login page
      window.location.href = '/login';
      throw createApiError(401, 'Session expired. Please login again.');
    }

    const errorText = await response.text();
    // Try to parse as JSON to extract error message
    let errorMessage = errorText || response.statusText;
    try {
      const errorJson = JSON.parse(errorText);
      errorMessage = errorJson.error || errorJson.message || errorJson.title || errorText;
    } catch {
      // Not JSON, use raw text
    }
    throw createApiError(response.status, errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json();
}

// Portfolio API
export const portfolioApi = {
  getAll: () => fetchApi<Portfolio[]>('/portfolios'),

  getById: (id: string) => fetchApi<Portfolio>(`/portfolios/${id}`),

  getSummary: (id: string, currentPrices?: Record<string, { price: number; exchangeRate: number }>) => {
    if (currentPrices && Object.keys(currentPrices).length > 0) {
      return fetchApi<PortfolioSummary>(`/portfolios/${id}/summary`, {
        method: 'POST',
        body: JSON.stringify({ currentPrices }),
      });
    }
    return fetchApi<PortfolioSummary>(`/portfolios/${id}/summary`);
  },

  calculateXirr: (id: string, request: CalculateXirrRequest) =>
    fetchApi<XirrResult>(`/portfolios/${id}/xirr`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  calculatePositionXirr: (
    portfolioId: string,
    ticker: string,
    request: { currentPrice?: number; currentExchangeRate?: number; asOfDate?: string }
  ) =>
    fetchApi<XirrResult>(`/portfolios/${portfolioId}/positions/${encodeURIComponent(ticker)}/xirr`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  create: (data: CreatePortfolioRequest) =>
    fetchApi<Portfolio>('/portfolios', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  update: (id: string, data: UpdatePortfolioRequest) =>
    fetchApi<Portfolio>(`/portfolios/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  delete: (id: string) =>
    fetchApi<void>(`/portfolios/${id}`, { method: 'DELETE' }),

  // Historical Performance
  getAvailableYears: (portfolioId: string) =>
    fetchApi<AvailableYears>(`/portfolios/${portfolioId}/performance/years`),

  calculateYearPerformance: (portfolioId: string, request: CalculateYearPerformanceRequest) =>
    fetchApi<YearPerformance>(`/portfolios/${portfolioId}/performance/year`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),
};

// Stock Transaction API
export const transactionApi = {
  getByPortfolio: (portfolioId: string) =>
    fetchApi<StockTransaction[]>(`/stocktransactions?portfolioId=${portfolioId}`),

  getById: (id: string) =>
    fetchApi<StockTransaction>(`/stocktransactions/${id}`),

  create: (data: CreateStockTransactionRequest) =>
    fetchApi<StockTransaction>('/stocktransactions', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  update: (id: string, data: UpdateStockTransactionRequest) =>
    fetchApi<StockTransaction>(`/stocktransactions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  delete: (id: string) =>
    fetchApi<void>(`/stocktransactions/${id}`, { method: 'DELETE' }),
};

// Health Check
export const healthApi = {
  check: () => fetchApi<{ status: string; timestamp: string }>('/health'),
};

// Auth API
export const authApi = {
  login: (data: LoginRequest) =>
    fetchApi<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  register: (data: RegisterRequest) =>
    fetchApi<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  refresh: (refreshToken: string) =>
    fetchApi<AuthResponse>('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),

  logout: (refreshToken: string) =>
    fetchApi<void>('/auth/logout', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),

  getMe: () =>
    fetchApi<{ id: string; email: string; displayName: string }>('/auth/me'),

  updateProfile: (data: { displayName?: string; email?: string }) =>
    fetchApi<{ id: string; email: string; displayName: string }>('/auth/me', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  changePassword: (data: { currentPassword: string; newPassword: string }) =>
    fetchApi<void>('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

// Currency Ledger API
export const currencyLedgerApi = {
  getAll: () => fetchApi<CurrencyLedgerSummary[]>('/currencyledgers'),

  getById: (id: string) =>
    fetchApi<CurrencyLedgerSummary>(`/currencyledgers/${id}`),

  create: (data: CreateCurrencyLedgerRequest) =>
    fetchApi<CurrencyLedger>('/currencyledgers', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  update: (id: string, data: UpdateCurrencyLedgerRequest) =>
    fetchApi<CurrencyLedger>(`/currencyledgers/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  delete: (id: string) =>
    fetchApi<void>(`/currencyledgers/${id}`, { method: 'DELETE' }),
};

// Currency Transaction API
export const currencyTransactionApi = {
  getByLedger: (ledgerId: string) =>
    fetchApi<CurrencyTransaction[]>(`/currencytransactions/ledger/${ledgerId}`),

  create: (data: CreateCurrencyTransactionRequest) =>
    fetchApi<CurrencyTransaction>('/currencytransactions', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  update: (id: string, data: UpdateCurrencyTransactionRequest) =>
    fetchApi<CurrencyTransaction>(`/currencytransactions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  delete: (id: string) =>
    fetchApi<void>(`/currencytransactions/${id}`, { method: 'DELETE' }),
};

// Stock Price API

interface CachedRate {
  rate: number;
  cachedAt: string;
}

const normalizeCurrency = (currency: string) => currency.trim().toUpperCase();

const getRateCacheKey = (from: string, to: string) =>
  `rate_cache_${normalizeCurrency(from)}_${normalizeCurrency(to)}`;

const loadCachedRateValue = (from: string, to: string): number | null => {
  try {
    const cached = localStorage.getItem(getRateCacheKey(from, to));
    if (!cached) return null;
    const data: CachedRate = JSON.parse(cached);
    return typeof data.rate === 'number' && data.rate > 0 ? data.rate : null;
  } catch {
    return null;
  }
};

const saveRateToCache = (from: string, to: string, rate: number): void => {
  try {
    localStorage.setItem(
      getRateCacheKey(from, to),
      JSON.stringify({ rate, cachedAt: new Date().toISOString() })
    );
  } catch {
    // Ignore cache errors
  }
};

const inFlightRates = new Map<string, Promise<number | null>>();

const getMarketBaseCurrency = (market: StockMarket): string => {
  // Keep in sync with backend MarketCurrencies mapping:
  // TW -> TWD, US/UK -> USD
  if (market === 1) return 'TWD';
  return 'USD';
};

async function resolveExchangeRateValue(from: string, to: string): Promise<number | null> {
  const fromCur = normalizeCurrency(from);
  const toCur = normalizeCurrency(to);

  if (!fromCur || !toCur) return null;
  if (fromCur === toCur) return 1;

  const cached = loadCachedRateValue(fromCur, toCur);
  if (cached) return cached;

  const inFlightKey = `${fromCur}_${toCur}`;
  const existing = inFlightRates.get(inFlightKey);
  if (existing) return existing;

  const promise = fetchApi<ExchangeRateResponse>(
    `/stock-prices/exchange-rate?from=${encodeURIComponent(fromCur)}&to=${encodeURIComponent(toCur)}`
  )
    .then((resp) => (resp?.rate && resp.rate > 0 ? resp.rate : null))
    .catch(() => null)
    .then((rate) => {
      if (rate) saveRateToCache(fromCur, toCur, rate);
      return rate;
    })
    .finally(() => {
      inFlightRates.delete(inFlightKey);
    });

  inFlightRates.set(inFlightKey, promise);
  return promise;
}

async function ensureQuoteHasExchangeRate(
  quote: StockQuoteResponse,
  homeCurrency: string
): Promise<StockQuoteResponse> {
  if (quote.exchangeRate && quote.exchangeRate > 0) return quote;

  const baseCurrency = getMarketBaseCurrency(quote.market);
  const rate = await resolveExchangeRateValue(baseCurrency, homeCurrency);
  if (!rate) return quote;

  return {
    ...quote,
    exchangeRate: rate,
    exchangeRatePair: `${baseCurrency}/${normalizeCurrency(homeCurrency)}`,
  };
}

export const stockPriceApi = {
  getQuote: (market: StockMarket, symbol: string) =>
    fetchApi<StockQuoteResponse>(`/stock-prices?market=${market}&symbol=${encodeURIComponent(symbol)}`),

  getQuoteWithRate: async (market: StockMarket, symbol: string, homeCurrency: string) => {
    const quote = await fetchApi<StockQuoteResponse>(
      `/stock-prices/with-rate?market=${market}&symbol=${encodeURIComponent(symbol)}&homeCurrency=${encodeURIComponent(homeCurrency)}`
    );
    return ensureQuoteHasExchangeRate(quote, homeCurrency);
  },

  getQuoteWithResolvedRate: async (market: StockMarket, symbol: string, homeCurrency: string) => {
    const quote = await fetchApi<StockQuoteResponse>(
      `/stock-prices?market=${market}&symbol=${encodeURIComponent(symbol)}`
    );
    return ensureQuoteHasExchangeRate(quote, homeCurrency);
  },

  getExchangeRate: (from: string, to: string) =>
    fetchApi<ExchangeRateResponse>(`/stock-prices/exchange-rate?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),

  getMarkets: () =>
    fetchApi<MarketInfo[]>('/stock-prices/markets'),
};

// Market Data API (CAPE & YTD)
export const marketDataApi = {
  getCapeData: () =>
    fetchApi<CapeData>('/market-data/cape'),

  refreshCapeData: () =>
    fetchApi<CapeData>('/market-data/cape/refresh', { method: 'POST' }),

  getYtdComparison: () =>
    fetchApi<MarketYtdComparison>('/market-data/ytd-comparison'),

  refreshYtdComparison: () =>
    fetchApi<MarketYtdComparison>('/market-data/ytd-comparison/refresh', { method: 'POST' }),

  getEuronextQuote: (isin: string, mic: string, homeCurrency: string = 'TWD', refresh: boolean = false) =>
    fetchApi<EuronextQuoteResponse>(
      `/market-data/euronext/quote?isin=${encodeURIComponent(isin)}&mic=${encodeURIComponent(mic)}&homeCurrency=${encodeURIComponent(homeCurrency)}&refresh=${refresh}`
    ),

  getHistoricalPrice: (ticker: string, date: string) =>
    fetchApi<{ price: number; currency: string; actualDate: string }>(
      `/market-data/historical-price?ticker=${encodeURIComponent(ticker)}&date=${encodeURIComponent(date)}`
    ),

  getHistoricalPrices: (tickers: string[], date: string) =>
    fetchApi<Record<string, { price: number; currency: string; actualDate: string }>>(
      '/market-data/historical-prices',
      {
        method: 'POST',
        body: JSON.stringify({ tickers, date }),
      }
    ),

  getHistoricalExchangeRate: (from: string, to: string, date: string) =>
    fetchApi<{ rate: number; fromCurrency: string; toCurrency: string; actualDate: string }>(
      `/market-data/historical-exchange-rate?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&date=${encodeURIComponent(date)}`
    ),

  getBenchmarkReturns: (year: number) =>
    fetchApi<{ year: number; returns: Record<string, number | null>; hasStartPrices: boolean; hasEndPrices: boolean }>(
      `/market-data/benchmark-returns?year=${year}`
    ),

  saveManualYearEndPrice: (data: {
    ticker: string;
    year: number;
    price: number;
    currency?: string;
    actualDate?: string;
  }) =>
    fetchApi<{ price: number; currency: string; actualDate: string; source: string; fromCache: boolean }>(
      '/market-data/year-end-price',
      {
        method: 'POST',
        body: JSON.stringify(data),
      }
    ),

  saveManualYearEndExchangeRate: (data: {
    fromCurrency: string;
    toCurrency: string;
    year: number;
    rate: number;
    actualDate?: string;
  }) =>
    fetchApi<{ rate: number; currencyPair: string; actualDate: string; source: string; fromCache: boolean }>(
      '/market-data/year-end-exchange-rate',
      {
        method: 'POST',
        body: JSON.stringify(data),
      }
    ),
};

// ETF Classification API
export const etfClassificationApi = {
  getClassification: (ticker: string) =>
    fetchApi<EtfClassificationResult>(`/etf-classification/${encodeURIComponent(ticker)}`),

  getAllClassifications: () =>
    fetchApi<EtfClassificationResult[]>('/etf-classification'),

  needsDividendAdjustment: (ticker: string) =>
    fetchApi<boolean>(`/etf-classification/${encodeURIComponent(ticker)}/needs-dividend-adjustment`),
};

export type { ApiErrorType };
