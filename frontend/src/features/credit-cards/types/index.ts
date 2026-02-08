export interface CreditCardResponse {
  id: string;
  bankName: string;
  cardName: string;
  billingCycleDay: number;
  note: string | null;
  isActive: boolean;
  activeInstallmentsCount: number;
  totalUnpaidBalance: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCreditCardRequest {
  bankName: string;
  cardName: string;
  billingCycleDay: number;
  note?: string | null;
}

export interface UpdateCreditCardRequest {
  bankName: string;
  cardName: string;
  billingCycleDay: number;
  note?: string | null;
}
