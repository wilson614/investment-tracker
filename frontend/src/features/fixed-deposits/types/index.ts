export interface FixedDepositResponse {
  id: string;
  bankAccountId: string;
  bankAccountName: string;
  principal: number;
  annualInterestRate: number;
  termMonths: number;
  startDate: string;
  maturityDate: string;
  expectedInterest: number;
  actualInterest: number | null;
  currency: string;
  status: 'Active' | 'Matured' | 'Closed' | 'EarlyWithdrawal';
  note: string | null;
  daysRemaining: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFixedDepositRequest {
  bankAccountId: string;
  principal: number;
  annualInterestRate: number;
  termMonths: number;
  startDate: string;
  note?: string | null;
}

export interface UpdateFixedDepositRequest {
  actualInterest?: number | null;
  note?: string | null;
}

export interface CloseFixedDepositRequest {
  actualInterest: number;
  isEarlyWithdrawal?: boolean;
}
