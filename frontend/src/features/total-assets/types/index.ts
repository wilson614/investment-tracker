import type { AllocationPurpose } from '../../fund-allocations/types';

export interface FundAllocationSummary {
  purpose: string;
  purposeDisplay: string;
  amount: number;
  percentage: number;
}

export interface AllocationBreakdown {
  purpose: AllocationPurpose;
  purposeDisplayName: string;
  amount: number;
}

export interface TotalAssetsSummary {
  investmentTotal: number;
  bankTotal: number;
  grandTotal: number;
  portfolioValue: number;
  cashBalance: number;
  disposableDeposit: number;
  nonDisposableDeposit: number;
  investmentRatio: number;
  stockRatio: number;
  investmentPercentage: number;
  bankPercentage: number;
  totalMonthlyInterest: number;
  totalYearlyInterest: number;
  totalAllocated: number;
  unallocated: number;
  allocationBreakdown?: AllocationBreakdown[];

  // Backward compatibility with existing UI fields (to be removed in later cleanup)
  allocations?: FundAllocationSummary[];
  unallocatedAmount?: number;
  hasOverAllocation?: boolean;
}

