// Portfolio and Transaction Types

// Auth Types
export interface User {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: User;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

// Portfolio Type Enum
export const PortfolioType = {
  Primary: 0,
  ForeignCurrency: 1,
} as const;
export type PortfolioType = (typeof PortfolioType)[keyof typeof PortfolioType];

export interface Portfolio {
  id: string;
  description?: string;
  baseCurrency: string;
  homeCurrency: string;
  isActive: boolean;
  portfolioType: PortfolioType;
  displayName?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreatePortfolioRequest {
  description?: string;
  baseCurrency: string;
  homeCurrency: string;
  portfolioType?: PortfolioType;
  displayName?: string;
}

export interface UpdatePortfolioRequest {
  description?: string;
}

export const TransactionType = {
  Buy: 1,
  Sell: 2,
  Split: 3,
  Adjustment: 4,
} as const;
export type TransactionType = (typeof TransactionType)[keyof typeof TransactionType];

export const FundSource = {
  None: 0,
  CurrencyLedger: 1,
} as const;
export type FundSource = (typeof FundSource)[keyof typeof FundSource];

export interface StockTransaction {
  id: string;
  portfolioId: string;
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate?: number; // Nullable - when null, cost is tracked in source currency only
  fees: number;
  fundSource: FundSource;
  currencyLedgerId?: string;
  notes?: string;
  totalCostSource: number;
  totalCostHome?: number; // Nullable - null when no exchange rate
  hasExchangeRate: boolean; // Indicates if transaction has exchange rate for home currency conversion
  realizedPnlHome?: number;
  createdAt: string;
  updatedAt: string;
  // Split adjustment fields (FR-052a)
  adjustedShares?: number;
  adjustedPricePerShare?: number;
  splitRatio: number;
  hasSplitAdjustment: boolean;
  market: StockMarket;
}

export interface CreateStockTransactionRequest {
  portfolioId: string;
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate?: number; // Optional when using CurrencyLedger - auto-calculated from ledger
  fees: number;
  fundSource?: FundSource;
  currencyLedgerId?: string;
  notes?: string;
  market?: StockMarket;
}

export interface UpdateStockTransactionRequest {
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate?: number; // Optional - if not provided, transaction cost tracked in source currency only
  fees: number;
  fundSource?: FundSource;
  currencyLedgerId?: string;
  notes?: string;
  market?: StockMarket;
}

export interface StockPosition {
  ticker: string;
  totalShares: number;
  totalCostHome?: number; // Nullable - null/undefined when no exchange rate data in position
  totalCostSource: number;
  averageCostPerShareHome?: number; // Nullable - null/undefined when no exchange rate data in position
  averageCostPerShareSource: number;
  currentPrice?: number;
  currentExchangeRate?: number;
  currentValueHome?: number;
  unrealizedPnlHome?: number;
  unrealizedPnlPercentage?: number;
}

export interface PortfolioSummary {
  portfolio: Portfolio;
  positions: StockPosition[];
  totalCostHome: number;
  totalValueHome?: number;
  totalUnrealizedPnlHome?: number;
  totalUnrealizedPnlPercentage?: number;
}

// Currency Ledger Types
export const CurrencyTransactionType = {
  ExchangeBuy: 1,
  ExchangeSell: 2,
  Interest: 3,
  Spend: 4,
  InitialBalance: 5,
  OtherIncome: 6,
  OtherExpense: 7,
} as const;
export type CurrencyTransactionType = (typeof CurrencyTransactionType)[keyof typeof CurrencyTransactionType];

export interface CurrencyLedger {
  id: string;
  currencyCode: string;
  name: string;
  homeCurrency: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CurrencyTransaction {
  id: string;
  currencyLedgerId: string;
  transactionDate: string;
  transactionType: CurrencyTransactionType;
  foreignAmount: number;
  homeAmount?: number;
  exchangeRate?: number;
  relatedStockTransactionId?: string;
  notes?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CurrencyLedgerSummary {
  ledger: CurrencyLedger;
  balance: number;
  averageExchangeRate: number;
  totalExchanged: number;
  totalSpentOnStocks: number;
  totalInterest: number;
  totalCost: number;
  realizedPnl: number;
  currentExchangeRate?: number;
  currentValueHome?: number;
  unrealizedPnlHome?: number;
  unrealizedPnlPercentage?: number;
  recentTransactions: CurrencyTransaction[];
}

export interface CreateCurrencyLedgerRequest {
  currencyCode: string;
  name: string;
  homeCurrency?: string;
}

export interface UpdateCurrencyLedgerRequest {
  name: string;
}

export interface CreateCurrencyTransactionRequest {
  currencyLedgerId: string;
  transactionDate: string;
  transactionType: CurrencyTransactionType;
  foreignAmount: number;
  homeAmount?: number;
  exchangeRate?: number;
  relatedStockTransactionId?: string;
  notes?: string;
}

export interface UpdateCurrencyTransactionRequest {
  transactionDate: string;
  transactionType: CurrencyTransactionType;
  foreignAmount: number;
  homeAmount?: number;
  exchangeRate?: number;
  notes?: string;
}

// Performance Types
export interface CurrentPriceInfo {
  price: number;
  exchangeRate: number;
}

export interface CalculateXirrRequest {
  currentPrices?: Record<string, CurrentPriceInfo>;
  asOfDate?: string;
}

export interface XirrResult {
  xirr: number | null;
  xirrPercentage: number | null;
  cashFlowCount: number;
  asOfDate: string;
}

// Stock Price Types
export const StockMarket = {
  TW: 1,
  US: 2,
  UK: 3,
  EU: 4,
} as const;
export type StockMarket = (typeof StockMarket)[keyof typeof StockMarket];

export interface StockQuoteResponse {
  symbol: string;
  name: string;
  price: number;
  change?: number;
  changePercent?: string;
  market: StockMarket;
  source: string;
  fetchedAt: string;
  exchangeRate?: number;
  exchangeRatePair?: string;
}

export interface ExchangeRateResponse {
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  source: string;
  fetchedAt: string;
}

export interface MarketInfo {
  value: number;
  name: string;
  description: string;
}

// CAPE (Cyclically Adjusted P/E) Types
export interface CapeDataItem {
  boxName: string;
  currentValue: number;
  adjustedValue?: number; // Real-time adjusted CAPE value based on current index price
  currentValuePercentile: number;
  range25th: number;
  range50th: number;
  range75th: number;
}

export interface CapeData {
  date: string;
  items: CapeDataItem[];
  fetchedAt: string;
}

export type CapeValuation = 'cheap' | 'fair' | 'expensive';

export interface CapeDisplayItem {
  region: string;
  cape: number;
  adjustedCape?: number; // Real-time adjusted value
  percentile: number;
  valuation: CapeValuation;
  median: number;
  range25th: number;
  range75th: number;
}

// Market YTD (Year-to-Date) Types
export interface MarketYtdReturn {
  marketKey: string;
  symbol: string;
  name: string;
  jan1Price: number | null;
  currentPrice: number | null;
  ytdReturnPercent: number | null;
  fetchedAt: string | null;
  error: string | null;
}

export interface MarketYtdComparison {
  year: number;
  benchmarks: MarketYtdReturn[];
  generatedAt: string;
}

// Euronext Quote Types
export interface EuronextQuoteResponse {
  price: number;
  currency: string;
  marketTime: string | null;
  name: string | null;
  exchangeRate: number | null;
  fromCache: boolean;
  changePercent?: string | null;
  change?: number | null;
}

// Historical Performance Types
export interface YearPerformance {
  year: number;
  // Home currency (TWD)
  xirr: number | null;
  xirrPercentage: number | null;
  totalReturnPercentage: number | null;
  startValueHome: number | null;
  endValueHome: number | null;
  netContributionsHome: number;
  // Source currency (e.g., USD)
  sourceCurrency: string | null;
  xirrSource: number | null;
  xirrPercentageSource: number | null;
  totalReturnPercentageSource: number | null;
  startValueSource: number | null;
  endValueSource: number | null;
  netContributionsSource: number | null;
  // Common
  cashFlowCount: number;
  transactionCount: number;
  missingPrices: MissingPrice[];
  isComplete: boolean;
}

export interface MissingPrice {
  ticker: string;
  date: string;
  priceType: 'YearStart' | 'YearEnd';
}

export interface AvailableYears {
  years: number[];
  earliestYear: number | null;
  currentYear: number;
}

export interface CalculateYearPerformanceRequest {
  year: number;
  yearEndPrices?: Record<string, YearEndPriceInfo>;
  yearStartPrices?: Record<string, YearEndPriceInfo>;
}

export interface YearEndPriceInfo {
  price: number;
  exchangeRate: number;
}

// ETF Classification Types
export type EtfTypeValue = 'Unknown' | 'Accumulating' | 'Distributing';

export interface EtfClassificationResult {
  ticker: string;
  type: EtfTypeValue;
  isConfirmed: boolean;
  source: string | null;
}

// User Benchmark Types
export interface UserBenchmark {
  id: string;
  ticker: string;
  market: StockMarket;
  displayName?: string;
  addedAt: string;
}

export interface CreateUserBenchmarkRequest {
  ticker: string;
  market: StockMarket;
  displayName?: string;
}
