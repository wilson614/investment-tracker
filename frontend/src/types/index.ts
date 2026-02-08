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

export interface Portfolio {
  id: string;
  description?: string;
  baseCurrency: string;
  homeCurrency: string;
  isActive: boolean;
  displayName?: string;
  boundCurrencyLedgerId: string; // Required - 1:1 binding with ledger
  createdAt: string;
  updatedAt: string;
}

export interface CreatePortfolioRequest {
  currencyCode: string; // Required - the currency for this portfolio (e.g., "USD", "TWD")
  description?: string;
  displayName?: string;
  homeCurrency?: string; // Defaults to "TWD"
  initialBalance?: number; // Optional initial balance for the bound ledger
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
  currencyLedgerId?: string; // Auto-set from portfolio's bound ledger
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
  currency: Currency;
}

export interface CreateStockTransactionRequest {
  portfolioId: string;
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate?: number; // Optional - auto-calculated from transaction date if not provided
  fees: number;
  autoDeposit?: boolean;
  notes?: string;
  market?: StockMarket;
  currency?: Currency;
}

export interface UpdateStockTransactionRequest {
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate?: number; // Optional - if not provided, transaction cost tracked in source currency only
  fees: number;
  autoDeposit?: boolean;
  notes?: string;
  market?: StockMarket;
  currency?: Currency;
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

  // Home currency
  currentValueHome?: number;
  unrealizedPnlHome?: number;
  unrealizedPnlPercentage?: number;

  // Source currency
  currentValueSource?: number;
  unrealizedPnlSource?: number;
  unrealizedPnlSourcePercentage?: number;

  market?: StockMarket;
  currency?: string;
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
  Deposit: 8,
  Withdraw: 9,
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

export interface MissingExchangeRate {
  transactionDate: string;
  currency: string;
}

export interface XirrResult {
  xirr: number | null;
  xirrPercentage: number | null;
  cashFlowCount: number;
  asOfDate: string;
  earliestTransactionDate: string | null;
  missingExchangeRates: MissingExchangeRate[] | null;
}

// Stock Price Types
export const StockMarket = {
  TW: 1,
  US: 2,
  UK: 3,
  EU: 4,
} as const;
export type StockMarket = (typeof StockMarket)[keyof typeof StockMarket];

export const Currency = {
  TWD: 1,
  USD: 2,
  GBP: 3,
  EUR: 4,
} as const;
export type Currency = (typeof Currency)[keyof typeof Currency];

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
  modifiedDietzPercentage: number | null;
  timeWeightedReturnPercentage: number | null;
  startValueHome: number | null;
  endValueHome: number | null;
  netContributionsHome: number;
  // Source currency (e.g., USD)
  sourceCurrency: string | null;
  xirrSource: number | null;
  xirrPercentageSource: number | null;
  totalReturnPercentageSource: number | null;
  modifiedDietzPercentageSource: number | null;
  timeWeightedReturnPercentageSource: number | null;
  startValueSource: number | null;
  endValueSource: number | null;
  netContributionsSource: number | null;
  // Common
  cashFlowCount: number;
  transactionCount: number;
  earliestTransactionDateInYear: string | null;
  missingPrices: MissingPrice[];
  isComplete: boolean;
}

export interface MissingPrice {
  ticker: string;
  date: string;
  priceType: 'YearStart' | 'YearEnd';
  market?: StockMarket;
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

export interface MonthlyNetWorth {
  month: string;
  value: number | null;
  contributions: number | null;
}

export interface MonthlyNetWorthHistory {
  data: MonthlyNetWorth[];
  currency: string;
  totalMonths: number;
  dataSource: string;
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

// Stock Split Types
export interface StockSplit {
  id: string;
  symbol: string;
  market: StockMarket;
  splitDate: string;
  splitRatio: number;
  description?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateStockSplitRequest {
  symbol: string;
  market: StockMarket;
  splitDate: string;
  splitRatio: number;
  description?: string;
}

export interface UpdateStockSplitRequest {
  splitDate: string;
  splitRatio: number;
  description?: string;
}

// Bank Account Types - Now using feature-based types
export type {
  BankAccount,
  BankAccountType,
  FixedDepositStatus,
  CloseBankAccountRequest,
  CreateBankAccountRequest,
  UpdateBankAccountRequest
} from '../features/bank-accounts/types';
