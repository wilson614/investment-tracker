export type AllocationPurpose =
  | 'EmergencyFund'
  | 'FamilyDeposit'
  | 'General'
  | 'Savings'
  | 'Investment'
  | 'Other'
  | (string & {});

export interface FundAllocation {
  id: string;
  purpose: AllocationPurpose;
  purposeDisplayName: string;
  amount: number;
  isDisposable: boolean;
  note?: string;
  createdAt: string;
  updatedAt: string;
}

export interface AllocationSummary {
  totalAllocated: number;
  unallocated: number;
  allocations: FundAllocation[];
}

export interface CreateFundAllocationRequest {
  purpose: AllocationPurpose;
  amount: number;
  isDisposable?: boolean;
}

export interface UpdateFundAllocationRequest {
  purpose?: AllocationPurpose;
  amount?: number;
  isDisposable?: boolean;
}
