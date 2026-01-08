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
export const stockPriceApi = {
  getQuote: (market: StockMarket, symbol: string) =>
    fetchApi<StockQuoteResponse>(`/stock-prices?market=${market}&symbol=${encodeURIComponent(symbol)}`),

  getQuoteWithRate: (market: StockMarket, symbol: string, homeCurrency: string) =>
    fetchApi<StockQuoteResponse>(`/stock-prices/with-rate?market=${market}&symbol=${encodeURIComponent(symbol)}&homeCurrency=${encodeURIComponent(homeCurrency)}`),

  getExchangeRate: (from: string, to: string) =>
    fetchApi<ExchangeRateResponse>(`/stock-prices/exchange-rate?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),

  getMarkets: () =>
    fetchApi<MarketInfo[]>('/stock-prices/markets'),
};

// Market Data API (CAPE)
export const marketDataApi = {
  getCapeData: () =>
    fetchApi<CapeData>('/market-data/cape'),

  refreshCapeData: () =>
    fetchApi<CapeData>('/market-data/cape/refresh', { method: 'POST' }),
};

export type { ApiErrorType };
