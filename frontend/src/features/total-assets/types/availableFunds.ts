export interface AvailableFundsSummaryResponse {
  totalBankAssets: number;
  availableFunds: number;
  committedFunds: number;
  breakdown: CommittedFundsBreakdown;
  currency: string;
}

export interface CommittedFundsBreakdown {
  fixedDepositsPrincipal: number;
  unpaidInstallmentBalance: number;
  fixedDeposits: FixedDepositSummary[];
  installments: InstallmentSummary[];
}

export interface FixedDepositSummary {
  id: string;
  bankName: string;
  principal: number;
  currency: string;
  principalInBaseCurrency: number;
}

export interface InstallmentSummary {
  id: string;
  description: string;
  creditCardName: string;
  unpaidBalance: number;
}
