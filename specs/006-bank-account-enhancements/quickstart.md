# Quickstart: Bank Account Enhancements

**Feature**: 006-bank-account-enhancements
**Date**: 2026-02-06

## Prerequisites

- .NET 8 SDK
- Node.js 20+ with pnpm
- PostgreSQL (running via Docker Compose)
- Existing InvestmentTracker development environment

## Getting Started

### 1. Database Migration

```bash
cd backend/src/InvestmentTracker.API
dotnet ef migrations add AddCurrencyAndFundAllocation
dotnet ef database update
```

### 2. Backend Development

Key files to modify/create:

| File | Action | Priority |
|------|--------|----------|
| `Domain/Entities/BankAccount.cs` | Add Currency field | P1 |
| `Domain/Entities/FundAllocation.cs` | Create new entity | P2 |
| `Domain/Enums/AllocationPurpose.cs` | Create enum | P2 |
| `Application/Services/HistoricalPerformanceService.cs` | Generalize currency | P3 |
| `Infrastructure/Repositories/FundAllocationRepository.cs` | Create repository | P2 |
| `API/Controllers/FundAllocationsController.cs` | Create controller | P2 |

### 3. Frontend Development

Key files to modify/create:

| File | Action | Priority |
|------|--------|----------|
| `utils/currency.ts` | Create formatting utility | P4 |
| `features/bank-accounts/types/index.ts` | Add currency field | P1 |
| `features/bank-accounts/components/BankAccountForm.tsx` | Add currency selector | P1 |
| `features/bank-accounts/components/BankAccountCard.tsx` | Fix interestCap, add currency | P1, P5 |
| `features/fund-allocations/` | Create entire feature | P2 |
| `features/total-assets/types/index.ts` | Add allocations | P2 |

### 4. Running Tests

```bash
# Backend
cd backend
dotnet test

# Frontend
cd frontend
pnpm test
```

### 5. Running the Application

```bash
# Terminal 1: Backend
cd backend/src/InvestmentTracker.API
dotnet run

# Terminal 2: Frontend
cd frontend
pnpm dev
```

## Implementation Order

### Phase 1: P5 Bug Fix (Quick Win)
1. Fix `interestCap != null` check in BankAccountCard.tsx

### Phase 2: P1 Multi-Currency
1. Add Currency field to BankAccount entity
2. Create migration
3. Update BankAccount DTOs and UseCases
4. Update BankAccountForm with currency selector
5. Update BankAccountCard to display currency
6. Create currency formatting utility

### Phase 3: P2 Fund Allocations
1. Create FundAllocation entity and AllocationPurpose enum
2. Create FundAllocationRepository
3. Create FundAllocation UseCases (CRUD)
4. Create FundAllocationsController
5. Update TotalAssetsService to include allocations
6. Create frontend fund-allocations feature
7. Update TotalAssetsBanner to show allocations

### Phase 4: P3 Historical Performance
1. Refactor GetUsdToTwdRate → GetSourceToHomeRate
2. Update calculation logic for TWD-based portfolios
3. Add unit tests for multi-currency scenarios

### Phase 5: P4 Display Consistency
1. Audit all currency displays in bank-accounts feature
2. Replace inconsistent formatting with utility function
3. Verify consistency across all components

## Key Decisions

- Currency stored as ISO 4217 string (e.g., "TWD", "USD")
- Fund allocations are virtual/mental accounting only
- Exchange rates use existing ExchangeRateService
- Over-allocation shows warning but doesn't block save (just prevents new allocations)

## Verification Checklist

- [ ] Can create bank account with foreign currency
- [ ] Total assets shows correct TWD conversion
- [ ] Can create fund allocations
- [ ] Over-allocation prevented
- [ ] Historical performance works for TWD portfolios
- [ ] Currency formatting consistent across app
- [ ] InterestCap=0 displays correctly
