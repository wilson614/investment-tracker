# Tasks: UX Enhancements & Market Selection

**Input**: Design documents from `/specs/003-ux-enhancements/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included per project convention (unit tests for domain logic, integration tests for critical paths).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `backend/src/InvestmentTracker.{Layer}/`
- **Frontend**: `frontend/src/`
- **Tests**: `backend/tests/`, `frontend/tests/`

---

## Phase 1: Setup ✅

**Purpose**: Database migrations and shared infrastructure

- [x] T001 Create EF Core migration for Market field in StockTransaction in `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [x] T002 Create EF Core migration for StockSplit table in `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [x] T003 Create EF Core migration for UserBenchmark table in `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [x] T004 Apply all migrations and verify database schema

---

## Phase 2: Foundational (Blocking Prerequisites) ✅

**Purpose**: Core entities and enums that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 [P] Add StockMarket enum to `backend/src/InvestmentTracker.Domain/Enums/StockMarket.cs` (TW=0, US=1, UK=2, EU=3)
- [x] T006 [P] Add Market property to StockTransaction entity in `backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs`
- [x] T007 [P] Create StockSplit entity in `backend/src/InvestmentTracker.Domain/Entities/StockSplit.cs`
- [x] T008 [P] Create UserBenchmark entity in `backend/src/InvestmentTracker.Domain/Entities/UserBenchmark.cs`
- [x] T009 Update AppDbContext with new DbSets in `backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs`
- [x] T010 Add entity configurations for StockSplit and UserBenchmark in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/`

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Transaction Market Selection (Priority: P1) 🎯 MVP ✅

**Goal**: Add Market field to transactions, allowing users to select market during creation with auto-prediction

**Independent Test**: Create transaction with manual market selection, refresh page, verify market persists and correct API is used for quotes

### Implementation for User Story 1

- [x] T011 [P] [US1] Create GuessMarketService in `backend/src/InvestmentTracker.Domain/Services/GuessMarketService.cs` (實作於 StockTransaction.GuessMarketFromTicker)
- [x] T012 [P] [US1] Add unit tests for GuessMarketService in `backend/tests/InvestmentTracker.Domain.Tests/Services/GuessMarketServiceTests.cs`
- [x] T013 [US1] Update CreateStockTransactionUseCase to handle Market field in `backend/src/InvestmentTracker.Application/UseCases/Transactions/CreateStockTransactionUseCase.cs`
- [x] T014 [US1] Update UpdateStockTransactionUseCase to handle Market field in `backend/src/InvestmentTracker.Application/UseCases/Transactions/UpdateStockTransactionUseCase.cs`
- [x] T015 [US1] Update TransactionController DTOs to include Market in `backend/src/InvestmentTracker.API/Controllers/TransactionController.cs`
- [x] T016 [US1] Update GetPortfolioSummaryUseCase to include Market in position response in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetPortfolioSummaryUseCase.cs`
- [x] T017 [P] [US1] Add market dropdown to TransactionForm in `frontend/src/components/transactions/TransactionForm.tsx`
- [x] T018 [US1] Add guessMarket utility function in `frontend/src/utils/marketUtils.ts` (實作於 TransactionForm.tsx 內)
- [x] T019 [US1] Update PositionCard to display market label (read-only) in `frontend/src/components/portfolio/PositionCard.tsx`
- [x] T020 [US1] Update quote fetching logic to use transaction market in `frontend/src/pages/Portfolio.tsx`
- [x] T021 [US1] Update quote fetching logic in Dashboard to use transaction market in `frontend/src/pages/Dashboard.tsx`
- [x] T022 [US1] Add integration test for market selection persistence in `backend/tests/InvestmentTracker.API.Tests/Integration/MarketSelectionTests.cs`

**Checkpoint**: Transaction market selection is fully functional and testable independently

---

## Phase 4: User Story 2 - Benchmark Custom Stocks (Priority: P1) ✅

**Goal**: Allow users to add custom stocks as benchmark comparisons with dividend warning for non-accumulating ETFs

**Independent Test**: Add custom benchmark stock, verify it appears on Performance page with historical data

### Implementation for User Story 2

- [x] T023 [P] [US2] Create UserBenchmark repository interface in `backend/src/InvestmentTracker.Application/Interfaces/IUserBenchmarkRepository.cs` (實作於 Domain)
- [x] T024 [P] [US2] Implement UserBenchmarkRepository in `backend/src/InvestmentTracker.Infrastructure/Persistence/Repositories/UserBenchmarkRepository.cs`
- [x] T025 [P] [US2] Create GetUserBenchmarksUseCase in `backend/src/InvestmentTracker.Application/UseCases/Benchmark/GetUserBenchmarksUseCase.cs`
- [x] T026 [P] [US2] Create AddUserBenchmarkUseCase in `backend/src/InvestmentTracker.Application/UseCases/Benchmark/AddUserBenchmarkUseCase.cs`
- [x] T027 [P] [US2] Create DeleteUserBenchmarkUseCase in `backend/src/InvestmentTracker.Application/UseCases/Benchmark/DeleteUserBenchmarkUseCase.cs`
- [x] T028 [US2] Create UserBenchmarkController in `backend/src/InvestmentTracker.API/Controllers/UserBenchmarkController.cs`
- [x] T029 [P] [US2] Create BenchmarkSettings component in `frontend/src/components/settings/BenchmarkSettings.tsx`
- [x] T030 [US2] Add benchmark API service in `frontend/src/services/benchmarkApi.ts`
- [x] T031 [US2] Integrate custom benchmarks into Performance page in `frontend/src/pages/Performance.tsx`
- [x] T032 [US2] Add dividend warning display for distributing ETFs in Performance page

**Checkpoint**: Benchmark custom stocks is fully functional and testable independently

---

## Phase 5: User Story 3 - Dashboard Historical Value Chart (Priority: P2) ✅

**Goal**: Display line chart showing portfolio value changes over years on Dashboard

**Independent Test**: View Dashboard with historical transactions, verify line chart displays with correct year-end values

### Implementation for User Story 3

- [x] T033 [P] [US3] Create HistoricalValueChart component in `frontend/src/components/dashboard/HistoricalValueChart.tsx`
- [x] T034 [US3] Integrate HistoricalValueChart into Dashboard in `frontend/src/pages/Dashboard.tsx`
- [x] T035 [US3] Add year-end data API call to Dashboard (use existing performance API)

**Checkpoint**: Dashboard historical chart is fully functional and testable independently

---

## Phase 6: User Story 4 - Stock Split Settings UI (Priority: P2) ✅

**Goal**: CRUD interface in Settings page for shared stock split data with automatic position recalculation

**Independent Test**: Add stock split record, verify position share counts adjust correctly

### Implementation for User Story 4

- [x] T036 [P] [US4] Create StockSplit repository interface in `backend/src/InvestmentTracker.Application/Interfaces/IStockSplitRepository.cs` (實作於 Domain)
- [x] T037 [P] [US4] Implement StockSplitRepository in `backend/src/InvestmentTracker.Infrastructure/Persistence/Repositories/StockSplitRepository.cs`
- [x] T038 [P] [US4] Create GetStockSplitsUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplit/GetStockSplitsUseCase.cs`
- [x] T039 [P] [US4] Create CreateStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplit/CreateStockSplitUseCase.cs`
- [x] T040 [P] [US4] Create UpdateStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplit/UpdateStockSplitUseCase.cs`
- [x] T041 [P] [US4] Create DeleteStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplit/DeleteStockSplitUseCase.cs`
- [x] T042 [US4] Create StockSplitController in `backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs` (整合於 StockTransactionsController)
- [x] T043 [P] [US4] Create StockSplitAdjustmentService in `backend/src/InvestmentTracker.Domain/Services/StockSplitAdjustmentService.cs`
- [x] T044 [P] [US4] Add unit tests for StockSplitAdjustmentService in `backend/tests/InvestmentTracker.Domain.Tests/Services/StockSplitAdjustmentServiceTests.cs`
- [x] T045 [US4] Integrate split adjustment into GetPortfolioSummaryUseCase in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetPortfolioSummaryUseCase.cs`
- [x] T046 [P] [US4] Create StockSplitSettings component in `frontend/src/components/settings/StockSplitSettings.tsx`
- [x] T047 [US4] Add stock split API service in `frontend/src/services/stockSplitApi.ts`
- [x] T048 [US4] Add Stock Split section to Settings page in `frontend/src/pages/Settings.tsx`

**Checkpoint**: Stock split settings is fully functional and testable independently

---

## Phase 7: User Story 5 - Default to Dashboard After Login (Priority: P3) ✅

**Goal**: Redirect users to Dashboard instead of Portfolio page after login

**Independent Test**: Login and verify automatic redirect to Dashboard

### Implementation for User Story 5

- [x] T049 [US5] Update root route redirect in `frontend/src/App.tsx` to navigate to /dashboard
- [x] T050 [US5] Verify login flow redirects correctly (no changes needed if using route-based redirect)

**Checkpoint**: Login redirect is fully functional

---

## Phase 8: User Story 6 - Taiwan Timezone Display (Priority: P3) ✅

**Goal**: Convert all time displays to Taiwan time (UTC+8)

**Independent Test**: View transaction dates and verify they display in Taiwan timezone

### Implementation for User Story 6

- [x] T051 [P] [US6] Create formatToTaiwanTime utility in `frontend/src/utils/dateUtils.ts`
- [x] T052 [US6] Apply Taiwan timezone formatting to TransactionList in `frontend/src/components/transactions/TransactionList.tsx`
- [x] T053 [US6] Apply Taiwan timezone formatting to Dashboard in `frontend/src/pages/Dashboard.tsx`
- [x] T054 [US6] Apply Taiwan timezone formatting to PositionDetail in `frontend/src/pages/PositionDetail.tsx`

**Checkpoint**: All time displays use Taiwan timezone

---

## Phase 9: User Story 7 - Date Input Auto-Tab Optimization (Priority: P3) ✅

**Goal**: Auto-tab to month field after 4-digit year input

**Independent Test**: Enter "2024" in year field, verify cursor moves to month field

### Implementation for User Story 7

- [x] T055 [US7] Add year auto-tab handler to DateInput component in `frontend/src/components/common/DateInput.tsx` (使用原生 HTML5 date input，已具備此行為)
- [x] T056 [US7] Verify month auto-tab still works correctly (existing behavior)

**Checkpoint**: Date input auto-tab is fully functional

---

## Phase 10: User Story 8 - Fee Field Default Value Adjustment (Priority: P3) ✅

**Goal**: Fee field defaults to empty instead of 0

**Independent Test**: Open new transaction form, verify fee field is empty

### Implementation for User Story 8

- [x] T057 [US8] Remove default value 0 from fee field in `frontend/src/components/transactions/TransactionForm.tsx`
- [x] T058 [US8] Update form submission to treat empty fee as 0

**Checkpoint**: Fee field default is correct

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Final improvements and validation

- [ ] T059 [P] Run all backend unit tests and fix any failures
- [ ] T060 [P] Run all frontend tests and fix any failures
- [ ] T061 Build frontend and backend, verify no compilation errors
- [ ] T062 Run quickstart.md validation checklist
- [ ] T063 Update API documentation with new endpoints

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-10)**: All depend on Foundational phase completion
  - User stories can proceed in priority order (P1 → P2 → P3)
  - US1 and US2 are both P1, can be done in parallel
- **Polish (Phase 11)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 - No dependencies on other stories
- **US2 (P1)**: Can start after Phase 2 - No dependencies on other stories
- **US3 (P2)**: Can start after Phase 2 - Uses existing performance API
- **US4 (P2)**: Can start after Phase 2 - Affects position calculations (test carefully)
- **US5 (P3)**: Can start after Phase 2 - Simple route change
- **US6 (P3)**: Can start after Phase 2 - Frontend-only changes
- **US7 (P3)**: Can start after Phase 2 - Frontend-only changes
- **US8 (P3)**: Can start after Phase 2 - Frontend-only changes

### Parallel Opportunities

Within Phase 2:
- T005, T006, T007, T008 can all run in parallel

Within US1:
- T011, T012, T017, T018 can run in parallel

Within US2:
- T023, T024, T025, T026, T027, T029 can run in parallel

Within US4:
- T036-T041, T043, T044, T046 can run in parallel

---

## Parallel Example: User Story 1

```bash
# Launch parallel backend tasks:
Task: "Create GuessMarketService in backend/src/InvestmentTracker.Domain/Services/GuessMarketService.cs"
Task: "Add unit tests for GuessMarketService in backend/tests/InvestmentTracker.Domain.Tests/Services/GuessMarketServiceTests.cs"

# Launch parallel frontend tasks:
Task: "Add market dropdown to TransactionForm in frontend/src/components/transactions/TransactionForm.tsx"
Task: "Add guessMarket utility function in frontend/src/utils/marketUtils.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (migrations)
2. Complete Phase 2: Foundational (entities, DbContext)
3. Complete Phase 3: User Story 1 (market selection)
4. **STOP and VALIDATE**: Test market selection end-to-end
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Market Selection) → Test → Deploy (MVP!)
3. Add US2 (Benchmark) → Test → Deploy
4. Add US3-4 (Chart, Split) → Test → Deploy
5. Add US5-8 (UX Polish) → Test → Deploy

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- US4 (Stock Split) affects position calculations - test thoroughly with edge cases
