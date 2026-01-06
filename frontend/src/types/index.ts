// Portfolio and Transaction Types

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
