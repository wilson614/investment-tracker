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

- [x] T001 [P] Create AllocationPurpose enum in `backend/src/InvestmentTracker.Domain/Enums/AllocationPurpose.cs` ✅
- [x] T002 [P] Create currency formatting utility in `frontend/src/utils/currency.ts` ✅

---

## Phase 2: Foundational

**Purpose**: Core infrastructure that MUST be complete before user stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 Add Currency property to BankAccount entity in `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs` ✅
- [x] T004 Create migration for Currency field in `backend/src/InvestmentTracker.Infrastructure/Migrations/` (AddCurrencyToBankAccount) ✅
- [x] T005 Run migration and verify existing accounts have Currency=TWD ✅

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

- [x] T019 [P] [US2] Create FundAllocation entity in `backend/src/InvestmentTracker.Domain/Entities/FundAllocation.cs` ✅
- [x] T020 [P] [US2] Create IFundAllocationRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/IFundAllocationRepository.cs` ✅
- [x] T021 [US2] Create FundAllocationRepository implementation in `backend/src/InvestmentTracker.Infrastructure/Repositories/FundAllocationRepository.cs` ✅
- [x] T022 [US2] Create migration for FundAllocations table in `backend/src/InvestmentTracker.Infrastructure/Migrations/` ✅
- [x] T023 [US2] Register FundAllocation in DbContext in `backend/src/InvestmentTracker.Infrastructure/Data/ApplicationDbContext.cs` ✅

### Backend - DTOs

- [x] T024 [P] [US2] Create FundAllocationResponse DTO in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs` ✅
- [x] T025 [P] [US2] Create CreateFundAllocationRequest DTO in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs` ✅
- [x] T026 [P] [US2] Create UpdateFundAllocationRequest DTO in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs` ✅
- [x] T027 [P] [US2] Create AllocationSummary DTO in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs` ✅

### Backend - Use Cases

- [x] T028 [US2] Create GetFundAllocationsUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/GetFundAllocationsUseCase.cs` ✅
- [x] T029 [US2] Create CreateFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/CreateFundAllocationUseCase.cs` ✅
- [ ] T029a [US2] Add unit tests for over-allocation validation in `backend/tests/InvestmentTracker.Application.Tests/`
- [x] T030 [US2] Create UpdateFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/UpdateFundAllocationUseCase.cs` ✅
- [x] T031 [US2] Create DeleteFundAllocationUseCase in `backend/src/InvestmentTracker.Application/UseCases/FundAllocation/DeleteFundAllocationUseCase.cs` ✅

### Backend - Controller & Service Updates

- [x] T032 [US2] Create FundAllocationsController in `backend/src/InvestmentTracker.API/Controllers/FundAllocationsController.cs` ✅
- [x] T033 [US2] Register FundAllocation services in DI in `backend/src/InvestmentTracker.API/Program.cs` ✅
- [x] T034 [US2] Update TotalAssetsSummary to include allocations in `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs` ✅
- [x] T035 [US2] Update GetTotalAssetsSummaryUseCase to include allocations in `backend/src/InvestmentTracker.Application/UseCases/Assets/GetTotalAssetsSummaryUseCase.cs` ✅

### Frontend - Types & API

- [x] T036 [P] [US2] Create FundAllocation types in `frontend/src/features/fund-allocations/types/index.ts` ✅
- [x] T037 [US2] Create fund allocations API client in `frontend/src/features/fund-allocations/api/allocationsApi.ts` ✅
- [x] T038 [US2] Update TotalAssetsSummary type with allocations in `frontend/src/features/total-assets/types/index.ts` ✅

### Frontend - Components

- [x] T039 [US2] Create AllocationForm component in `frontend/src/features/fund-allocations/components/AllocationForm.tsx` ✅
- [x] T040 [US2] Create AllocationSummary component in `frontend/src/features/fund-allocations/components/AllocationSummary.tsx` ✅
- [x] T041 [US2] Update TotalAssetsBanner to show allocations in `frontend/src/features/total-assets/components/TotalAssetsBanner.tsx` ✅

**Checkpoint**: Fund allocation feature complete with dashboard display

---

## Phase 5.5: User Story 6 - Total Assets Dashboard Refactor with Disposable Fund Tracking (Priority: P2)

**Goal**: Refactor dashboard to show Investment Ratio and Stock Ratio, distinguish disposable vs non-disposable funds

**Independent Test**: Create fund allocations with different isDisposable settings, verify dashboard shows correct ratios

### Backend - Entity & Migration

- [ ] T053 [US6] Add IsDisposable field to FundAllocation entity in `backend/src/InvestmentTracker.Domain/Entities/FundAllocation.cs`
- [ ] T054 [US6] Add Investment and Other to AllocationPurpose enum in `backend/src/InvestmentTracker.Domain/Enums/AllocationPurpose.cs`
- [ ] T055 [US6] Create migration for IsDisposable field in `backend/src/InvestmentTracker.Infrastructure/Migrations/`
- [ ] T056 [US6] Add data migration: set IsDisposable based on purpose (false for EmergencyFund/FamilyDeposit, true for others)

### Backend - DTOs & Use Cases

- [ ] T057 [US6] Add IsDisposable to CreateFundAllocationRequest in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T058 [US6] Add IsDisposable to UpdateFundAllocationRequest in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T059 [US6] Add IsDisposable to FundAllocationResponse in `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs`
- [ ] T060 [US6] Update CreateFundAllocationUseCase to handle IsDisposable with default logic
- [ ] T061 [US6] Update UpdateFundAllocationUseCase to handle IsDisposable

### Backend - Summary Calculation

- [ ] T062 [US6] Add PortfolioValue, CashBalance to TotalAssetsSummary DTO
- [ ] T063 [US6] Add DisposableDeposit, NonDisposableDeposit to TotalAssetsSummary DTO
- [ ] T064 [US6] Add InvestmentRatio, StockRatio to TotalAssetsSummary DTO
- [ ] T065 [US6] Update GetTotalAssetsSummaryUseCase to calculate new fields

### Frontend - Types & API

- [ ] T066 [P] [US6] Add IsDisposable to FundAllocation types in `frontend/src/features/fund-allocations/types/index.ts`
- [ ] T067 [US6] Update TotalAssetsSummary type with new fields in `frontend/src/features/total-assets/types/index.ts`

### Frontend - Form Update

- [ ] T068 [US6] Add IsDisposable toggle to AllocationFormDialog in `frontend/src/features/fund-allocations/components/AllocationFormDialog.tsx`
- [ ] T069 [US6] Implement default IsDisposable logic based on purpose selection

### Frontend - Dashboard Refactor

- [ ] T070 [US6] Create CoreMetricsSection component in `frontend/src/features/total-assets/components/CoreMetricsSection.tsx`
- [ ] T071 [US6] Create MetricCard component in `frontend/src/features/total-assets/components/MetricCard.tsx`
- [ ] T072 [US6] Create DisposableAssetsSection component in `frontend/src/features/total-assets/components/DisposableAssetsSection.tsx`
- [ ] T073 [US6] Create NonDisposableAssetsSection component in `frontend/src/features/total-assets/components/NonDisposableAssetsSection.tsx`
- [ ] T074 [US6] Refactor AssetsBreakdownPieChart to 4 slices in `frontend/src/features/total-assets/components/AssetsBreakdownPieChart.tsx`
- [ ] T075 [US6] Refactor TotalAssetsDashboard layout in `frontend/src/features/total-assets/pages/TotalAssetsDashboard.tsx`

**Checkpoint**: Dashboard refactor complete with Investment Ratio and Stock Ratio

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

1. **Setup** (T001-T002) - ✅ Complete
2. **Foundational** (T003-T005) - ✅ Complete
3. **US5 Bug Fix** (T006) - Quick win first
4. **US1 Multi-Currency** (T007-T018) - Core feature
5. **US2 Fund Allocations** (T019-T041) - ✅ Complete
6. **US6 Dashboard Refactor** (T053-T075) - **NEW** Dashboard with Investment/Stock Ratio
7. **US3 Historical Performance** (T042-T045) - Backend refactor
8. **US4 Display Consistency** (T046-T048) - Polish
9. **Polish** (T049-T052) - Final verification

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

| Phase | User Story | Task Count | Parallel Tasks | Status |
|-------|------------|------------|----------------|--------|
| 1 | Setup | 2 | 2 | ✅ Complete |
| 2 | Foundational | 3 | 0 | ✅ Complete |
| 3 | US5 - Bug Fix | 1 | 0 | Pending |
| 4 | US1 - Multi-Currency | 14 | 6 | Pending |
| 5 | US2 - Fund Allocations | 24 | 7 | ✅ Complete |
| 5.5 | US6 - Dashboard Refactor | 23 | 1 | **NEW** |
| 6 | US3 - Historical Performance | 4 | 0 | Pending |
| 7 | US4 - Display Consistency | 3 | 0 | Pending |
| 8 | Polish | 4 | 0 | Pending |
| **Total** | | **78** | **16** | |

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
