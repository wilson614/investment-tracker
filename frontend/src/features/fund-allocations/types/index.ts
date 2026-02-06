export type AllocationPurpose = 'EmergencyFund' | 'FamilyDeposit' | 'General' | 'Savings' | 'Investment' | 'Other';

export interface FundAllocation {
  id: string;
  purpose: AllocationPurpose;
  purposeDisplayName: string;
  amount: number;
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
  note?: string;
}

export interface UpdateFundAllocationRequest {
  purpose?: AllocationPurpose;
  amount?: number;
  note?: string;
}
