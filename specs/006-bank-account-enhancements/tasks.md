# Tasks: Bank Account Enhancements

**Input**: Design documents from `/specs/006-bank-account-enhancements/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Not explicitly requested - only implementation tasks included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `backend/src/InvestmentTracker.{Layer}/`
- **Frontend**: `frontend/src/`

---

## Phase 1: Setup

**Purpose**: Database migrations and shared utilities

- [ ] T001 [P] Create AllocationPurpose enum in `backend/src/InvestmentTracker.Domain/Enums/AllocationPurpose.cs`
- [ ] T002 [P] Create currency formatting utility in `frontend/src/utils/currency.ts`

---

## Phase 2: Foundational

**Purpose**: Core infrastructure that MUST be complete before user stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 Add Currency property to BankAccount entity in `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs`
- [ ] T004 Create migration for Currency field in `backend/src/InvestmentTracker.Infrastructure/Migrations/` (AddCurrencyToBankAccount)
- [ ] T005 Run migration and verify existing accounts have Currency=TWD

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 5 - Interest Cap Zero Display Fix (Priority: P5) 🎯 QUICK WIN

**Goal**: Fix bug where interestCap=0 displays as "無上限" instead of "NT$ 0"

**Independent Test**: Create account with interestCap=0, verify it displays "NT$ 0" not "無上限"

### Implementation

- [ ] T006 [US5] Fix interestCap display logic in `frontend/src/features/bank-accounts/components/BankAccountCard.tsx` (change `account.interestCap ?` to `account.interestCap != null ?`)

**Checkpoint**: Bug fix complete - interestCap=0 displays correctly

---

## Phase 4: User Story 1 - Foreign Currency Bank Account Support (Priority: P1) 🎯 MVP

**Goal**: Users can create bank accounts in different currencies (TWD, USD, EUR, JPY, CNY, GBP, AUD)

**Independent Test**: Create a USD bank account, verify it displays with $ symbol and converts to TWD in total assets

### Backend Implementation

- [ ] T007 [P] [US1] Add Currency to CreateBankAccountRequest in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T008 [P] [US1] Add Currency to UpdateBankAccountRequest in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T009 [P] [US1] Add Currency to BankAccountResponse in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs`
- [ ] T010 [US1] Update CreateBankAccountUseCase to accept Currency in `backend/src/InvestmentTracker.Application/UseCases/BankAccount/CreateBankAccountUseCase.cs`
- [ ] T011 [US1] Update UpdateBankAccountUseCase to accept Currency in `backend/src/InvestmentTracker.Application/UseCases/BankAccount/UpdateBankAccountUseCase.cs`
- [ ] T012 [US1] Update TotalAssetsService to convert foreign currencies to TWD in `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs`
- [ ] T012a [US1] Add unit tests for TotalAssetsService multi-currency conversion in `backend/tests/InvestmentTracker.Application.Tests/`

### Frontend Implementation

- [ ] T013 [P] [US1] Add currency field to BankAccount type in `frontend/src/features/bank-accounts/types/index.ts`
- [ ] T014 [P] [US1] Add currency field to CreateBankAccountRequest in `frontend/src/features/bank-accounts/types/index.ts`
- [ ] T015 [P] [US1] Add currency field to UpdateBankAccountRequest in `frontend/src/features/bank-accounts/types/index.ts`
- [ ] T016 [US1] Add currency selector dropdown to BankAccountForm in `frontend/src/features/bank-accounts/components/BankAccountForm.tsx`
- [ ] T017 [US1] Update BankAccountCard to display currency using formatCurrency utility in `frontend/src/features/bank-accounts/components/BankAccountCard.tsx`
- [ ] T017a [US1] Add stale rate indicator UI when exchange rate is outdated (> 24 hours) in `frontend/src/features/bank-accounts/components/BankAccountCard.tsx`
- [ ] T018 [US1] Update BankAccountsPage to show currency per account in `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`

**Checkpoint**: Foreign currency bank accounts fully functional

---

## Phase 5: User Story 2 - Fund Allocation for Bank Assets (Priority: P2)

**Goal**: Users can allocate total bank assets to virtual purposes (Emergency Fund, Family Deposit, etc.)

**Independent Test**: Create fund allocations, verify dashboard shows breakdown with unallocated remainder

### Backend - Entity & Repository

- [ ] T019 [P] [US2] Create FundAllocation entity in `backend/src/InvestmentTracker.Domain/Entities/FundAllocation.cs`
- [ ] T020 [P] [US2] Create IFundAllocationRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/IFundAllocationRepository.cs`
- [ ] T021 [US2] Create FundAllocationRepository implementation in `backend/src/InvestmentTracker.Infrastructure/Repositories/FundAllocationRepository.cs`
- [ ] T022 [US2] Create migration for FundAllocations table in `backend/src/InvestmentTracker.Infrastructure/Migrations/`
- [ ] T023 [US2] Register FundAllocation in DbContext in `backend/src/InvestmentTracker.Infrastructure/Data/ApplicationDbContext.cs`

### Backend - DTOs

- [ ] T024 [P] [US2] Create FundAllocationResponse DTO in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs`
- [ ] T025 [P] [US2] Create CreateFundAllocationRequest DTO in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T026 [P] [US2] Create UpdateFundAllocationRequest DTO in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T027 [P] [US2] Create AllocationSummary DTO in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs`

### Backend - Use Cases

- [ ] T028 [US2] Create GetFundAllocationsUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/GetFundAllocationsUseCase.cs`
- [ ] T029 [US2] Create CreateFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/CreateFundAllocationUseCase.cs`
- [ ] T029a [US2] Add unit tests for over-allocation validation in `backend/tests/InvestmentTracker.Application.Tests/`
- [ ] T030 [US2] Create UpdateFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/UpdateFundAllocationUseCase.cs`
- [ ] T031 [US2] Create DeleteFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/DeleteFundAllocationUseCase.cs`

### Backend - Controller & Service Updates

- [ ] T032 [US2] Create FundAllocationsController in `backend/src/InvestmentTracker.API/Controllers/FundAllocationsController.cs`
- [ ] T033 [US2] Register FundAllocation services in DI in `backend/src/InvestmentTracker.API/Program.cs`
- [ ] T034 [US2] Update TotalAssetsSummary to include allocations in `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs`
- [ ] T035 [US2] Update GetTotalAssetsSummaryUseCase to include allocations in `backend/src/InvestmentTracker.Application/UseCases/Assets/GetTotalAssetsSummaryUseCase.cs`

### Frontend - Types & API

- [ ] T036 [P] [US2] Create FundAllocation types in `frontend/src/features/fund-allocations/types/index.ts`
- [ ] T037 [US2] Create fund allocations API client in `frontend/src/features/fund-allocations/api/allocationsApi.ts`
- [ ] T038 [US2] Update TotalAssetsSummary type with allocations in `frontend/src/features/total-assets/types/index.ts`

### Frontend - Components

- [ ] T039 [US2] Create AllocationForm component in `frontend/src/features/fund-allocations/components/AllocationForm.tsx`
- [ ] T040 [US2] Create AllocationSummary component in `frontend/src/features/fund-allocations/components/AllocationSummary.tsx`
- [ ] T041 [US2] Update TotalAssetsBanner to show allocations in `frontend/src/features/total-assets/components/TotalAssetsBanner.tsx`

**Checkpoint**: Fund allocation feature complete with dashboard display

---

## Phase 6: User Story 3 - Historical Performance Multi-Currency Support (Priority: P3)

**Goal**: Historical performance charts correctly handle TWD-based portfolios

**Independent Test**: Create TWD-based portfolio, verify performance chart shows correct values without conversion errors

### Implementation

- [ ] T042 [US3] Refactor GetUsdToTwdRate to GetSourceToHomeRate in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [ ] T043 [US3] Update ConvertAmountAsync to use portfolio.BaseCurrency in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [ ] T044 [US3] Handle TWD portfolios with exchange rate = 1.0 in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [ ] T045 [US3] Update GetYearlyPerformanceAsync to support any base currency in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`

**Checkpoint**: Historical performance works for all portfolio currencies

---

## Phase 7: User Story 4 - Currency Display Consistency (Priority: P4)

**Goal**: Consistent currency formatting across all bank account screens

**Independent Test**: Navigate through all bank account screens, verify all currency values use formatCurrency utility

### Implementation

- [ ] T046 [US4] Audit and update InterestEstimationCard currency display in `frontend/src/features/bank-accounts/components/InterestEstimationCard.tsx`
- [ ] T047 [US4] Verify BankAccountCard formatCurrency consistency (confirm T017 implementation covers all currency displays)
- [ ] T048 [US4] Verify BankAccountsPage uses formatCurrency consistently in `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`

**Checkpoint**: All currency displays use consistent formatting

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and cleanup

- [ ] T049 Verify all acceptance scenarios from spec.md
- [ ] T050 Run full build and verify no errors
- [ ] T051 Manual E2E test of complete workflow
- [ ] T052 Verify API documentation (Swagger/OpenAPI) reflects new endpoints and fields

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on T001 migration
- **User Story 5 (Phase 3)**: Can start after T003 (currency utility) - QUICK WIN
- **User Story 1 (Phase 4)**: Depends on Foundational (Phase 2)
- **User Story 2 (Phase 5)**: Depends on Foundational (Phase 2), can run parallel with US1
- **User Story 3 (Phase 6)**: No dependency on US1/US2, backend-only
- **User Story 4 (Phase 7)**: Depends on T003 (currency utility), can run after US1
- **Polish (Phase 8)**: Depends on all user stories complete

### Recommended Execution Order

1. **Setup** (T001-T003) - All can run in parallel
2. **Foundational** (T004-T005) - Sequential
3. **US5 Bug Fix** (T006) - Quick win first
4. **US1 Multi-Currency** (T007-T018) - Core feature
5. **US2 Fund Allocations** (T019-T041) - Large feature
6. **US3 Historical Performance** (T042-T045) - Backend refactor
7. **US4 Display Consistency** (T046-T048) - Polish
8. **Polish** (T049-T051) - Final verification

### Parallel Opportunities

**Phase 1 (all parallel)**:
```
T001, T002, T003 → Run together
```

**Phase 4 Backend (parallel DTOs)**:
```
T007, T008, T009 → Run together
```

**Phase 4 Frontend (parallel types)**:
```
T013, T014, T015 → Run together
```

**Phase 5 Backend (parallel entity/interface)**:
```
T019, T020 → Run together
```

**Phase 5 Backend (parallel DTOs)**:
```
T024, T025, T026, T027 → Run together
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: US5 Bug Fix (quick win)
4. Complete Phase 4: US1 Multi-Currency
5. **STOP and VALIDATE**: Test foreign currency accounts independently
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US5 Bug Fix → Quick win deployed
3. US1 Multi-Currency → Foreign currency support deployed
4. US2 Fund Allocations → Mental accounting deployed
5. US3 Historical Performance → Accurate charts for all portfolios
6. US4 Display Consistency → Polished UX
7. Each story adds value without breaking previous stories

---

## Summary

| Phase | User Story | Task Count | Parallel Tasks |
|-------|------------|------------|----------------|
| 1 | Setup | 2 | 2 |
| 2 | Foundational | 3 | 0 |
| 3 | US5 - Bug Fix | 1 | 0 |
| 4 | US1 - Multi-Currency | 14 | 6 |
| 5 | US2 - Fund Allocations | 24 | 7 |
| 6 | US3 - Historical Performance | 4 | 0 |
| 7 | US4 - Display Consistency | 3 | 0 |
| 8 | Polish | 4 | 0 |
| **Total** | | **55** | **15** |

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Tasks with suffix (e.g., T012a, T029a) are test/verification tasks added for constitution compliance
- Each user story should be independently completable and testable
- Commit after each task or logical group
- US5 is placed first as it's a quick win bug fix
- US1 is the core MVP feature
- US2 is the largest feature with 24 tasks (including test task)
