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
  MonthlyNetWorthHistory,
  UserBenchmark,
  CreateUserBenchmarkRequest,
  StockSplit,
  CreateStockSplitRequest,
  UpdateStockSplitRequest,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_URL || '/api';

const TOKEN_KEY = 'token';
const REFRESH_TOKEN_KEY = 'refreshToken';
const USER_KEY = 'user';

// ============================================================================
// API 錯誤處理
// ============================================================================

interface ApiErrorType extends Error {
  status: number;
}

function createApiError(status: number, message: string): ApiErrorType {
  const error = new Error(message) as ApiErrorType;
  error.name = 'ApiError';
  error.status = status;
  return error;
}

// ============================================================================
// Auth / Token Refresh Helpers (US6)
// ============================================================================

let inFlightRefresh: Promise<boolean> | null = null;

function removeLocalStorageByPrefixes(prefixes: readonly string[]): void {
  const keysToRemove: string[] = [];
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i);
    if (key && prefixes.some((prefix) => key.startsWith(prefix))) {
      keysToRemove.push(key);
    }
  }
  keysToRemove.forEach((key) => localStorage.removeItem(key));
}

function clearAuthData(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  localStorage.removeItem(USER_KEY);

  // 清除所有使用者相關的快取資料，防止帳號間資料洩漏
  removeLocalStorageByPrefixes(['quote_cache_', 'perf_cache_', 'xirr_cache_', 'rate_cache_']);

  // 清除使用者專屬市場資料快取
  localStorage.removeItem('custom_benchmark_ytd_cache');

  // 注意：ytd_data_cache 和 cape_data_cache 是公開市場資料，不需要清除

  // 導覽快取（不應跨帳號保留）
  localStorage.removeItem('default_portfolio_id');
  localStorage.removeItem('selected_portfolio_id');
}

function clearAuthAndRedirect(): void {
  clearAuthData();
  window.location.href = '/login';
}

function shouldAttachAuthHeader(endpoint: string): boolean {
  // Auth endpoints that should NOT include Authorization header
  return !(
    endpoint === '/auth/login' ||
    endpoint === '/auth/register' ||
    endpoint === '/auth/refresh' ||
    endpoint === '/auth/logout'
  );
}

function buildHeaders(optionsHeaders: HeadersInit | undefined, token: string | null): Headers {
  const headers = new Headers(optionsHeaders);
  if (!headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  return headers;
}

async function refreshAccessToken(): Promise<boolean> {
  if (inFlightRefresh) {
    return inFlightRefresh;
  }

  const storedRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  if (!storedRefreshToken) {
    return false;
  }

  const refreshPromise = (async () => {
    try {
      const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ refreshToken: storedRefreshToken }),
      });

      if (!response.ok) {
        return false;
      }

      const data = (await response.json()) as AuthResponse;

      localStorage.setItem(TOKEN_KEY, data.accessToken);
      localStorage.setItem(REFRESH_TOKEN_KEY, data.refreshToken);
      localStorage.setItem(USER_KEY, JSON.stringify(data.user));

      return true;
    } catch {
      return false;
    }
  })();

  inFlightRefresh = refreshPromise.finally(() => {
    inFlightRefresh = null;
  });

  return inFlightRefresh;
}

/**
 * 通用 API 請求函數
 * - 自動處理驗證 token
 * - 401 時嘗試 refresh token 後重試一次
 * - refresh 失敗才清除 session 並導向登入
 */
async function fetchApi<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const canUseAuthHeader = shouldAttachAuthHeader(endpoint);

  const execute = async (authToken: string | null): Promise<Response> => {
    const headers = buildHeaders(options.headers, authToken);

    return fetch(`${API_BASE_URL}${endpoint}`, {
      ...options,
      headers,
    });
  };

  const initialToken = canUseAuthHeader ? localStorage.getItem(TOKEN_KEY) : null;
  let response = await execute(initialToken);

  if (response.status === 401 && canUseAuthHeader) {
    // Prevent refresh loop: only retry once per request
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      const newToken = localStorage.getItem(TOKEN_KEY);
      response = await execute(newToken);
    }
  }

  if (!response.ok) {
    // 401: refresh failed or token invalid
    if (response.status === 401 && canUseAuthHeader) {
      clearAuthAndRedirect();
      throw createApiError(401, 'Session expired. Please login again.');
    }

    const errorText = await response.text();
    let errorMessage = errorText || response.statusText;
    try {
      const errorJson = JSON.parse(errorText) as Record<string, unknown>;
      const message =
        (typeof errorJson.error === 'string' && errorJson.error) ||
        (typeof errorJson.message === 'string' && errorJson.message) ||
        (typeof errorJson.title === 'string' && errorJson.title) ||
        null;
      errorMessage = message ?? errorText;
    } catch {
      // 非 JSON 格式，使用原始文字
    }
    throw createApiError(response.status, errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json();
}

// ============================================================================
// 投資組合 API
// ============================================================================

export const portfolioApi = {
  /** 取得所有投資組合 */
  getAll: () => fetchApi<Portfolio[]>('/portfolios'),

  /** 依 ID 取得投資組合 */
  getById: (id: string) => fetchApi<Portfolio>(`/portfolios/${id}`),

  /** 取得投資組合摘要（可傳入即時價格） */
  getSummary: (id: string, currentPrices?: Record<string, { price: number; exchangeRate: number }>) => {
    if (currentPrices && Object.keys(currentPrices).length > 0) {
      return fetchApi<PortfolioSummary>(`/portfolios/${id}/summary`, {
        method: 'POST',
        body: JSON.stringify({ currentPrices }),
      });
    }
    return fetchApi<PortfolioSummary>(`/portfolios/${id}/summary`);
  },

  /** 計算投資組合 XIRR */
  calculateXirr: (id: string, request: CalculateXirrRequest) =>
    fetchApi<XirrResult>(`/portfolios/${id}/xirr`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  /** 計算單一持股的 XIRR */
  calculatePositionXirr: (
    portfolioId: string,
    ticker: string,
    request: { currentPrice?: number; currentExchangeRate?: number; asOfDate?: string }
  ) =>
    fetchApi<XirrResult>(`/portfolios/${portfolioId}/positions/${encodeURIComponent(ticker)}/xirr`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  /** 建立新投資組合 */
  create: (data: CreatePortfolioRequest) =>
    fetchApi<Portfolio>('/portfolios', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新投資組合 */
  update: (id: string, data: UpdatePortfolioRequest) =>
    fetchApi<Portfolio>(`/portfolios/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 刪除投資組合 */
  delete: (id: string) =>
    fetchApi<void>(`/portfolios/${id}`, { method: 'DELETE' }),

  // 歷史績效
  /** 取得可用的歷史績效年份 */
  getAvailableYears: (portfolioId: string) =>
    fetchApi<AvailableYears>(`/portfolios/${portfolioId}/performance/years`),

  /** 計算年度績效 */
  calculateYearPerformance: (portfolioId: string, request: CalculateYearPerformanceRequest) =>
    fetchApi<YearPerformance>(`/portfolios/${portfolioId}/performance/year`, {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  /** 取得月度淨值資料 */
  getMonthlyNetWorth: (portfolioId: string, fromMonth?: string, toMonth?: string) => {
    const params = new URLSearchParams();
    if (fromMonth) params.set('fromMonth', fromMonth);
    if (toMonth) params.set('toMonth', toMonth);
    const query = params.toString();
    return fetchApi<MonthlyNetWorthHistory>(
      `/portfolios/${portfolioId}/performance/monthly${query ? `?${query}` : ''}`
    );
  },
};

// ============================================================================
// 股票交易 API
// ============================================================================

export const transactionApi = {
  /** 取得投資組合的所有交易 */
  getByPortfolio: (portfolioId: string) =>
    fetchApi<StockTransaction[]>(`/stocktransactions?portfolioId=${portfolioId}`),

  /** 依 ID 取得交易 */
  getById: (id: string) =>
    fetchApi<StockTransaction>(`/stocktransactions/${id}`),

  /** 建立新交易 */
  create: (data: CreateStockTransactionRequest) =>
    fetchApi<StockTransaction>('/stocktransactions', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新交易 */
  update: (id: string, data: UpdateStockTransactionRequest) =>
    fetchApi<StockTransaction>(`/stocktransactions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 刪除交易 */
  delete: (id: string) =>
    fetchApi<void>(`/stocktransactions/${id}`, { method: 'DELETE' }),
};

// ============================================================================
// 健康檢查 API
// ============================================================================

export const healthApi = {
  /** 檢查後端服務狀態 */
  check: () => fetchApi<{ status: string; timestamp: string }>('/health'),
};

// ============================================================================
// 驗證 API
// ============================================================================

export const authApi = {
  /** 登入 */
  login: (data: LoginRequest) =>
    fetchApi<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 註冊 */
  register: (data: RegisterRequest) =>
    fetchApi<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新 token */
  refresh: (refreshToken: string) =>
    fetchApi<AuthResponse>('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),

  /** 登出 */
  logout: (refreshToken: string) =>
    fetchApi<void>('/auth/logout', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),

  /** 取得目前使用者資訊 */
  getMe: () =>
    fetchApi<{ id: string; email: string; displayName: string }>('/auth/me'),

  /** 更新個人資料 */
  updateProfile: (data: { displayName?: string; email?: string }) =>
    fetchApi<{ id: string; email: string; displayName: string }>('/auth/me', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 變更密碼 */
  changePassword: (data: { currentPassword: string; newPassword: string }) =>
    fetchApi<void>('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

// ============================================================================
// 外幣帳戶 API
// ============================================================================

export const currencyLedgerApi = {
  /** 取得所有外幣帳戶 */
  getAll: () => fetchApi<CurrencyLedgerSummary[]>('/currencyledgers'),

  /** 依 ID 取得外幣帳戶 */
  getById: (id: string) =>
    fetchApi<CurrencyLedgerSummary>(`/currencyledgers/${id}`),

  /** 建立新外幣帳戶 */
  create: (data: CreateCurrencyLedgerRequest) =>
    fetchApi<CurrencyLedger>('/currencyledgers', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新外幣帳戶 */
  update: (id: string, data: UpdateCurrencyLedgerRequest) =>
    fetchApi<CurrencyLedger>(`/currencyledgers/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 刪除外幣帳戶 */
  delete: (id: string) =>
    fetchApi<void>(`/currencyledgers/${id}`, { method: 'DELETE' }),
};

// ============================================================================
// 外幣交易 API
// ============================================================================

export const currencyTransactionApi = {
  /** 取得外幣帳戶的所有交易 */
  getByLedger: (ledgerId: string) =>
    fetchApi<CurrencyTransaction[]>(`/currencytransactions/ledger/${ledgerId}`),

  /** 建立新外幣交易 */
  create: (data: CreateCurrencyTransactionRequest) =>
    fetchApi<CurrencyTransaction>('/currencytransactions', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新外幣交易 */
  update: (id: string, data: UpdateCurrencyTransactionRequest) =>
    fetchApi<CurrencyTransaction>(`/currencytransactions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 刪除外幣交易 */
  delete: (id: string) =>
    fetchApi<void>(`/currencytransactions/${id}`, { method: 'DELETE' }),
};

// ============================================================================
// 股票價格 API
// ============================================================================

// 匯率快取介面
interface CachedRate {
  rate: number;
  cachedAt: string;
}

/** 標準化幣別代碼 */
const normalizeCurrency = (currency: string) => currency.trim().toUpperCase();

/** 產生匯率快取鍵值 */
const getRateCacheKey = (from: string, to: string) =>
  `rate_cache_${normalizeCurrency(from)}_${normalizeCurrency(to)}`;

/** 從快取載入匯率 */
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

/** 儲存匯率至快取 */
const saveRateToCache = (from: string, to: string, rate: number): void => {
  try {
    localStorage.setItem(
      getRateCacheKey(from, to),
      JSON.stringify({ rate, cachedAt: new Date().toISOString() })
    );
  } catch {
    // 忽略快取錯誤
  }
};

// 進行中的匯率請求（防止重複呼叫）
const inFlightRates = new Map<string, Promise<number | null>>();

/** 取得市場基礎幣別 */
const getMarketBaseCurrency = (market: StockMarket): string => {
  // 與後端 MarketCurrencies 對應同步：
  // TW -> TWD, US/UK -> USD
  if (market === 1) return 'TWD';
  return 'USD';
};

/**
 * 解析匯率值
 * 優先從快取讀取，若無則呼叫 API
 */
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

/**
 * 確保報價包含匯率
 * 若報價中無匯率則自動補齊
 */
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
  /** 取得股票報價 */
  getQuote: (market: StockMarket, symbol: string) =>
    fetchApi<StockQuoteResponse>(`/stock-prices?market=${market}&symbol=${encodeURIComponent(symbol)}`),

  /** 取得包含匯率的股票報價 */
  getQuoteWithRate: async (market: StockMarket, symbol: string, homeCurrency: string) => {
    const quote = await fetchApi<StockQuoteResponse>(
      `/stock-prices/with-rate?market=${market}&symbol=${encodeURIComponent(symbol)}&homeCurrency=${encodeURIComponent(homeCurrency)}`
    );
    return ensureQuoteHasExchangeRate(quote, homeCurrency);
  },

  /** 取得報價並自動解析匯率 */
  getQuoteWithResolvedRate: async (market: StockMarket, symbol: string, homeCurrency: string) => {
    const quote = await fetchApi<StockQuoteResponse>(
      `/stock-prices?market=${market}&symbol=${encodeURIComponent(symbol)}`
    );
    return ensureQuoteHasExchangeRate(quote, homeCurrency);
  },

  /** 取得匯率 */
  getExchangeRate: (from: string, to: string) =>
    fetchApi<ExchangeRateResponse>(`/stock-prices/exchange-rate?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),

  /** 取得支援的市場清單 */
  getMarkets: () =>
    fetchApi<MarketInfo[]>('/stock-prices/markets'),
};

// ============================================================================
// 市場資料 API (CAPE & YTD)
// ============================================================================

export const marketDataApi = {
  /** 取得 CAPE 資料 */
  getCapeData: () =>
    fetchApi<CapeData>('/market-data/cape'),

  /** 強制重新取得 CAPE 資料 */
  refreshCapeData: () =>
    fetchApi<CapeData>('/market-data/cape/refresh', { method: 'POST' }),

  /** 取得 YTD 比較資料 */
  getYtdComparison: () =>
    fetchApi<MarketYtdComparison>('/market-data/ytd-comparison'),

  /** 強制重新取得 YTD 比較資料 */
  refreshYtdComparison: () =>
    fetchApi<MarketYtdComparison>('/market-data/ytd-comparison/refresh', { method: 'POST' }),

  /** 取得 Euronext 報價（使用 ISIN/MIC） */
  getEuronextQuote: (isin: string, mic: string, homeCurrency: string = 'TWD') =>
    fetchApi<EuronextQuoteResponse>(
      `/market-data/euronext/quote?isin=${encodeURIComponent(isin)}&mic=${encodeURIComponent(mic)}&homeCurrency=${encodeURIComponent(homeCurrency)}`
    ),

  /** 透過 ticker 取得 Euronext 報價（自動查詢 ISIN/MIC） */
  getEuronextQuoteByTicker: (ticker: string, homeCurrency: string = 'TWD') =>
    fetchApi<EuronextQuoteResponse>(
      `/market-data/euronext/quote-by-ticker?ticker=${encodeURIComponent(ticker)}&homeCurrency=${encodeURIComponent(homeCurrency)}`
    ),

  /** 取得歷史價格 */
  getHistoricalPrice: (ticker: string, date: string) =>
    fetchApi<{ price: number; currency: string; actualDate: string }>(
      `/market-data/historical-price?ticker=${encodeURIComponent(ticker)}&date=${encodeURIComponent(date)}`
    ),

  /** 批次取得歷史價格 */
  getHistoricalPrices: (tickers: string[], date: string, markets?: Record<string, number | null>) =>
    fetchApi<Record<string, { price: number; currency: string; actualDate: string }>>(
      '/market-data/historical-prices',
      {
        method: 'POST',
        body: JSON.stringify({ tickers, date, markets }),
      }
    ),

  /** 取得歷史匯率 */
  getHistoricalExchangeRate: (from: string, to: string, date: string) =>
    fetchApi<{ rate: number; fromCurrency: string; toCurrency: string; actualDate: string }>(
      `/market-data/historical-exchange-rate?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&date=${encodeURIComponent(date)}`
    ),

  /** 取得基準指數報酬 */
  getBenchmarkReturns: (year: number) =>
    fetchApi<{
      year: number;
      returns: Record<string, number | null>;
      hasStartPrices: boolean;
      hasEndPrices: boolean;
      dataSources: Record<string, string | null>;
    }>(`/market-data/benchmark-returns?year=${year}`),

  /** 手動儲存年末價格 */
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

  /** 手動儲存年末匯率 */
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

// ============================================================================
// ETF 分類 API
// ============================================================================

export const etfClassificationApi = {
  /** 取得 ETF 分類 */
  getClassification: (ticker: string) =>
    fetchApi<EtfClassificationResult>(`/etf-classification/${encodeURIComponent(ticker)}`),

  /** 取得所有 ETF 分類 */
  getAllClassifications: () =>
    fetchApi<EtfClassificationResult[]>('/etf-classification'),

  /** 檢查是否需要股息調整 */
  needsDividendAdjustment: (ticker: string) =>
    fetchApi<boolean>(`/etf-classification/${encodeURIComponent(ticker)}/needs-dividend-adjustment`),
};

// ============================================================================
// 使用者基準標的 API
// ============================================================================

export const userBenchmarkApi = {
  /** 取得使用者的所有基準標的 */
  getAll: () => fetchApi<UserBenchmark[]>('/user-benchmarks'),

  /** 新增基準標的 */
  create: (data: CreateUserBenchmarkRequest) =>
    fetchApi<UserBenchmark>('/user-benchmarks', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 刪除基準標的 */
  delete: (id: string) =>
    fetchApi<void>(`/user-benchmarks/${id}`, { method: 'DELETE' }),
};

// ============================================================================
// 股票分割 API
// ============================================================================

export const stockSplitApi = {
  /** 取得所有股票分割資料 */
  getAll: () => fetchApi<StockSplit[]>('/stock-splits'),

  /** 取得特定股票分割 */
  getById: (id: string) => fetchApi<StockSplit>(`/stock-splits/${id}`),

  /** 依股票代號與市場查詢 */
  getBySymbol: (symbol: string, market: StockMarket) =>
    fetchApi<StockSplit[]>(`/stock-splits/by-symbol?symbol=${encodeURIComponent(symbol)}&market=${market}`),

  /** 新增股票分割 */
  create: (data: CreateStockSplitRequest) =>
    fetchApi<StockSplit>('/stock-splits', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新股票分割 */
  update: (id: string, data: UpdateStockSplitRequest) =>
    fetchApi<StockSplit>(`/stock-splits/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 刪除股票分割 */
  delete: (id: string) =>
    fetchApi<void>(`/stock-splits/${id}`, { method: 'DELETE' }),
};

// ============================================================================
// 使用者偏好設定 API
// ============================================================================

export interface UserPreferences {
  ytdBenchmarkPreferences: string | null;
  capeRegionPreferences: string | null;
  defaultPortfolioId: string | null;
}

export interface UpdateUserPreferencesRequest {
  ytdBenchmarkPreferences?: string;
  capeRegionPreferences?: string;
  defaultPortfolioId?: string;
}

export const userPreferencesApi = {
  /** 取得使用者偏好設定 */
  get: () => fetchApi<UserPreferences>('/user-preferences'),

  /** 更新使用者偏好設定 */
  update: (data: UpdateUserPreferencesRequest) =>
    fetchApi<UserPreferences>('/user-preferences', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),
};

export type { ApiErrorType };
