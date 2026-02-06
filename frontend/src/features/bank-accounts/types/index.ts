// Bank Account Types

export interface BankAccount {
  id: string;
  userId: string;
  bankName: string;
  totalAssets: number;
  interestRate: number;      // Annual interest rate %
  interestCap?: number;       // Preferential interest cap
  note?: string;
  currency: string;
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
  currency: string; // Default "TWD" when creating
}

export interface UpdateBankAccountRequest {
  bankName: string;
  totalAssets: number;
  interestRate: number;
  interestCap?: number;
  note?: string;
  currency?: string;
}

export interface InterestEstimation {
  monthlyInterest: number;
  yearlyInterest: number;
}
