// Bank Account Types

export type BankAccountType = 'Savings' | 'FixedDeposit';
export type FixedDepositStatus = 'Active' | 'Matured' | 'Closed' | 'EarlyWithdrawal';

export interface BankAccount {
  id: string;
  userId: string;
  bankName: string;
  totalAssets: number;
  interestRate: number;      // Annual interest rate %
  interestCap?: number;      // Preferential interest cap
  note?: string;
  currency: string;
  accountType: BankAccountType;
  termMonths?: number;
  startDate?: string;
  maturityDate?: string;
  expectedInterest?: number;
  actualInterest?: number;
  fixedDepositStatus?: FixedDepositStatus;
  monthlyInterest: number;   // Calculated monthly interest
  yearlyInterest: number;    // Calculated annual interest
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateBankAccountRequest {
  bankName: string;
  totalAssets: number;
  interestRate: number;
  interestCap?: number;
  note?: string;
  currency?: string; // Optional, defaults to "TWD" when creating
  accountType: BankAccountType;
  termMonths?: number;
  startDate?: string;
}

export interface UpdateBankAccountRequest {
  bankName: string;
  totalAssets: number;
  interestRate: number;
  interestCap?: number;
  note?: string;
  currency?: string;
  accountType?: BankAccountType;
  termMonths?: number;
  startDate?: string;
  actualInterest?: number;
  fixedDepositStatus?: FixedDepositStatus;
}

export interface CloseBankAccountRequest {
  actualInterest?: number;
}

export interface InterestEstimation {
  monthlyInterest: number;
  yearlyInterest: number;
}
