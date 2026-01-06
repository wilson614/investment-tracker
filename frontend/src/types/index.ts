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
  name: string;
  description?: string;
  baseCurrency: string;
  homeCurrency: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreatePortfolioRequest {
  name: string;
  description?: string;
  baseCurrency: string;
  homeCurrency: string;
}

export interface UpdatePortfolioRequest {
  name: string;
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
  exchangeRate: number;
  fees: number;
  fundSource: FundSource;
  currencyLedgerId?: string;
  notes?: string;
  totalCostSource: number;
  totalCostHome: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateStockTransactionRequest {
  portfolioId: string;
  transactionDate: string;
  ticker: string;
  transactionType: TransactionType;
  shares: number;
  pricePerShare: number;
  exchangeRate: number;
  fees: number;
  fundSource?: FundSource;
  currencyLedgerId?: string;
  notes?: string;
}

export interface UpdateStockTransactionRequest {
  transactionDate: string;
  shares: number;
  pricePerShare: number;
  exchangeRate: number;
  fees: number;
  fundSource?: FundSource;
  currencyLedgerId?: string;
  notes?: string;
}

export interface StockPosition {
  ticker: string;
  totalShares: number;
  totalCostHome: number;
  averageCostPerShare: number;
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
  weightedAverageCost: number;
  totalCostHome: number;
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
