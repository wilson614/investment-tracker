export interface InstallmentResponse {
  id: string;
  creditCardId: string;
  creditCardName: string;
  description: string;
  totalAmount: number;
  numberOfInstallments: number;
  remainingInstallments: number;
  monthlyPayment: number;
  startDate: string;
  status: 'Active' | 'Completed' | 'Cancelled';
  note: string | null;
  unpaidBalance: number;
  paidAmount: number;
  progressPercentage: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateInstallmentRequest {
  creditCardId: string;
  description: string;
  totalAmount: number;
  numberOfInstallments: number;
  startDate: string;
  note?: string | null;
}

export interface UpdateInstallmentRequest {
  description: string;
  note?: string | null;
}

export interface UpcomingPaymentMonth {
  month: string; // YYYY-MM
  totalAmount: number;
  payments: Array<{
    installmentId: string;
    description: string;
    creditCardName: string;
    amount: number;
  }>;
}
