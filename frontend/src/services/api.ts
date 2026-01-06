import type {
  Portfolio,
  CreatePortfolioRequest,
  UpdatePortfolioRequest,
  StockTransaction,
  CreateStockTransactionRequest,
  UpdateStockTransactionRequest,
  PortfolioSummary,
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
    throw createApiError(response.status, errorText || response.statusText);
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
    const params = currentPrices
      ? `?${new URLSearchParams({ currentPrices: JSON.stringify(currentPrices) })}`
      : '';
    return fetchApi<PortfolioSummary>(`/portfolios/${id}/summary${params}`);
  },

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

export type { ApiErrorType };
