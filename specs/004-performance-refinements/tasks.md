# Tasks: Performance Refinements

**Input**: Design documents from `/specs/004-performance-refinements/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Web app**: `backend/src/`, `frontend/src/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database migrations and DI registration for new entities

- [ ] T001 Create `MonthlyNetWorthSnapshot` entity in backend/src/InvestmentTracker.Domain/Entities/MonthlyNetWorthSnapshot.cs
- [ ] T002 [P] Create `BenchmarkAnnualReturn` entity in backend/src/InvestmentTracker.Domain/Entities/BenchmarkAnnualReturn.cs
- [ ] T003 Add DbSet properties and configure entity mappings in backend/src/InvestmentTracker.Infrastructure/Persistence/ApplicationDbContext.cs
- [ ] T004 Create entity configurations in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/
- [ ] T005 Generate migration: `dotnet ef migrations add AddPerformanceRefinementsTables`
- [ ] T006 Apply migration to development database and verify indexes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core services and DTOs that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T007 Update `CurrencyTransactionType` to add `Deposit` / `Withdraw` in backend/src/InvestmentTracker.Domain/Enums/CurrencyTransactionType.cs
- [ ] T008 Create `IReturnCalculator` interface in backend/src/InvestmentTracker.Domain/Interfaces/IReturnCalculator.cs
- [ ] T009 Implement `ReturnCalculator` (Modified Dietz + TWR) in backend/src/InvestmentTracker.Domain/Services/ReturnCalculator.cs
- [ ] T010 [P] Write unit tests in backend/tests/InvestmentTracker.Domain.Tests/Services/ReturnCalculatorTests.cs
- [ ] T011 [P] Add `UnrealizedPnlSource`, `UnrealizedPnlSourcePercentage`, `CurrentValueSource` to `StockPositionDto` in backend/src/InvestmentTracker.Application/DTOs/PositionDtos.cs
- [ ] T012 [P] Create `MonthlyNetWorthDto` and `MonthlyNetWorthHistoryDto` in backend/src/InvestmentTracker.Application/DTOs/MonthlyNetWorthDtos.cs
- [ ] T013 [P] Create `BenchmarkReturnDto` with `DataSource` field in backend/src/InvestmentTracker.Application/DTOs/BenchmarkReturnDtos.cs
- [ ] T014 [P] Update `YearPerformanceDto` to include Modified Dietz + TWR fields (home/source) in backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - View Annual Returns with Dual Metrics (Priority: P1) 🎯 MVP

**Goal**: Display Modified Dietz Return (investor timing) + TWR (stock selection) with CF source strategy and per-event portfolio value snapshots

**Independent Test**: Performance page displays both Modified Dietz and TWR for selected year in both source and home currency; values are stable across refresh

### Implementation for User Story 1

- [ ] T015 [US1] Create `TransactionPortfolioSnapshot` entity in backend/src/InvestmentTracker.Domain/Entities/TransactionPortfolioSnapshot.cs
- [ ] T016 [US1] Add DbSet and entity mappings in backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs
- [ ] T017 [US1] Create entity configuration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/TransactionPortfolioSnapshotConfiguration.cs
- [ ] T018 [US1] Generate migration for TransactionPortfolioSnapshot + CurrencyTransactionType changes in backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/
- [ ] T019 [US1] Create `IReturnCashFlowStrategy` + implementations (StockTransaction / CurrencyLedger external in/out) in backend/src/InvestmentTracker.Domain/Services/ReturnCashFlowStrategy.cs
- [ ] T020 [US1] Create `ITransactionPortfolioSnapshotService` in backend/src/InvestmentTracker.Application/Interfaces/ITransactionPortfolioSnapshotService.cs
- [ ] T021 [US1] Implement `TransactionPortfolioSnapshotService` in backend/src/InvestmentTracker.Infrastructure/Services/TransactionPortfolioSnapshotService.cs (re-use price/FX fetching patterns from MonthlySnapshotService)
- [ ] T022 [US1] Register new services in backend/src/InvestmentTracker.API/Program.cs (ReturnCalculator, CashFlowStrategy, SnapshotService)
- [ ] T023 [US1] On StockTransaction create/update/delete, write or invalidate per-event snapshots in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs, UpdateStockTransactionUseCase.cs, DeleteStockTransactionUseCase.cs
- [ ] T024 [US1] If ledger mode enabled, ensure Deposit/Withdraw currency transactions also write/invalidate snapshots in backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/ (relevant use cases)
- [ ] T025 [US1] Update HistoricalPerformanceService to compute Modified Dietz + TWR using snapshots and CF strategy in backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs
- [ ] T026 [P] [US1] Update TypeScript types in frontend/src/types/index.ts (new YearPerformance fields)
- [ ] T027 [US1] Update Performance UI to display two metrics (Modified Dietz + TWR) in frontend/src/pages/Performance.tsx

**Checkpoint**: Performance page shows Modified Dietz + TWR (home/source) - Story 1 complete

---

## Phase 4: User Story 2 - Toggle Currency Mode (Priority: P1)

**Goal**: Allow users to switch between source currency and home currency display for performance comparison

**Independent Test**: Toggle appears in performance comparison, switching updates chart, preference persists

### Implementation for User Story 2

- [ ] T022 [P] [US2] Create `CurrencyToggle.tsx` component with localStorage persistence in frontend/src/components/performance/CurrencyToggle.tsx
- [ ] T023 [P] [US2] Create tests for CurrencyToggle in frontend/src/components/performance/CurrencyToggle.test.tsx
- [ ] T024 [US2] Add `CurrencyToggle` to performance comparison section in frontend/src/pages/Performance.tsx
- [ ] T025 [US2] Wire toggle state to chart data selection (source vs home currency mode)
- [ ] T026 [US2] Ensure benchmark values remain unchanged regardless of toggle state

**Checkpoint**: Currency toggle works and persists - Story 2 complete

---

## Phase 5: User Story 3 - Source Currency P&L (Priority: P2)

**Goal**: Display unrealized P&L in source currency on position detail page to distinguish stock gains from currency effects

**Independent Test**: Position detail shows source currency P&L with format `+$1,200 USD (+15%)`

### Implementation for User Story 3

- [ ] T027 [US3] Calculate `CurrentValueSource`, `UnrealizedPnlSource`, `UnrealizedPnlSourcePercentage` in backend/src/InvestmentTracker.Application/Services/PortfolioSummaryService.cs
- [ ] T028 [US3] Map source P&L fields to DTO in GetPortfolioSummaryAsync response
- [ ] T029 [US3] Update TypeScript types for position with source P&L fields in frontend/src/types/index.ts (depends on T019 completion to avoid merge conflicts)
- [ ] T030 [US3] Display source P&L in PositionDetail metrics section with color coding in frontend/src/pages/PositionDetail.tsx

**Checkpoint**: Position detail shows source currency P&L - Story 3 complete

---

## Phase 6: User Story 4 - Monthly Net Worth (Priority: P2)

**Goal**: Upgrade historical net worth chart from yearly to monthly data points

**Independent Test**: Chart displays monthly data points with YYYY-MM format X-axis labels

### Implementation for User Story 4

- [ ] T031 [US4] Create `IMonthlySnapshotService` interface in backend/src/InvestmentTracker.Application/Interfaces/IMonthlySnapshotService.cs
- [ ] T032 [US4] Implement `MonthlySnapshotService` with historical price fetching (Yahoo primary; Stooq/TWSE fallback) in backend/src/InvestmentTracker.Infrastructure/Services/MonthlySnapshotService.cs
- [ ] T033 [US4] Implement range-based price strategy (fetch full daily series per ticker for requested range and derive month-end points server-side)
- [ ] T034 [US4] Implement cache invalidation on transaction create/update/delete (delete snapshots from affected month onwards)
- [ ] T035 [P] [US4] Write integration tests in backend/tests/InvestmentTracker.Application.Tests/Services/MonthlySnapshotServiceTests.cs
- [ ] T036 [US4] Add `GetMonthlyNetWorth` endpoint with fromMonth/toMonth params in backend/src/InvestmentTracker.API/Controllers/PerformanceController.cs (include current month as-of today)
- [ ] T037 [P] [US4] Add `getMonthlyNetWorth` API method in frontend/src/services/api.ts
- [ ] T038 [US4] Update Dashboard to use monthly net worth API instead of per-year performance calls in frontend/src/pages/Dashboard.tsx
- [ ] T039 [US4] Update `HistoricalValueChart` to display monthly labels (YYYY-MM) in frontend/src/components/dashboard/HistoricalValueChart.tsx and handle 60+ points

**Checkpoint**: Chart shows monthly net worth data - Story 4 complete

---

## Phase 7: User Story 5 - Benchmark Total Return (Priority: P3)

**Goal**: Fetch Yahoo Annual Total Return for accurate dividend-inclusive benchmark comparison (replacing current price-based calculation)

**Independent Test**: Historical year comparison shows Yahoo Total Return with DataSource indicator

**Note**: Current `GET /api/market-data/benchmark-returns` uses year-end price snapshots to calculate Price Return. This story upgrades it to use Yahoo's Annual Total Return (including dividends).

### Implementation for User Story 5

- [ ] T039 [US5] Add `GetAnnualTotalReturnAsync` method to existing `IYahooHistoricalPriceService` interface in backend/src/InvestmentTracker.Domain/Interfaces/IYahooHistoricalPriceService.cs
- [ ] T040 [US5] Implement `GetAnnualTotalReturnAsync` in `YahooHistoricalPriceService` - fetch from Yahoo Performance page in backend/src/InvestmentTracker.Infrastructure/MarketData/YahooHistoricalPriceService.cs
- [ ] T041 [US5] Create `BenchmarkAnnualReturn` cache entity and repository for storing fetched Total Returns
- [ ] T042 [US5] Implement fallback to existing price-based calculation when Yahoo Total Return unavailable
- [ ] T043 [P] [US5] Write tests in backend/tests/InvestmentTracker.Domain.Tests/Services/YahooHistoricalPriceServiceTests.cs
- [ ] T044 [US5] Modify existing `GetBenchmarkReturns` endpoint to prefer Yahoo Total Return over price calculation in backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs
- [ ] T045 [US5] Add `DataSource` field to `BenchmarkReturnsResponse` (Yahoo / Calculated)
- [ ] T046 [P] [US5] Update frontend to display DataSource indicator in frontend/src/pages/Performance.tsx

**Checkpoint**: Benchmark shows Yahoo Total Return - Story 5 complete

---

## Phase 8: User Story 6 - Login Session (Priority: P3)

**Goal**: Extend login session to 2+ hours and implement proper token refresh mechanism

**Independent Test**: User stays logged in for 2+ hours of active use without unexpected logouts

### Implementation for User Story 6

- [ ] T047 [US6] Update default Access Token expiration from 15 to 120 minutes in backend/src/InvestmentTracker.Infrastructure/Services/JwtTokenService.cs
- [ ] T048 [P] [US6] Verify environment variable `Jwt__AccessTokenExpirationMinutes` override works
- [ ] T049 [US6] Verify `/api/auth/refresh` endpoint exists and works correctly
- [ ] T050 [US6] Modify `fetchApi` to attempt token refresh on 401 before clearing session in frontend/src/services/api.ts
- [ ] T051 [US6] Implement retry with new token after successful refresh
- [ ] T052 [US6] Add refresh loop prevention flag to avoid infinite retries
- [ ] T053 [US6] Update stored tokens in localStorage after successful refresh

**Checkpoint**: Token refresh works smoothly - Story 6 complete

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and documentation

- [ ] T054 [P] Run backend tests: `dotnet test`
- [ ] T055 [P] Run frontend tests: `npm test`
- [ ] T056 Fix any failing tests
- [ ] T057 [P] Update Swagger/OpenAPI comments for new endpoints
- [ ] T058 Run quickstart.md verification checklist
- [ ] T059 Manual testing: US1 - Performance page shows Simple Return
- [ ] T060 [P] Manual testing: US2 - Currency toggle works and persists
- [ ] T061 [P] Manual testing: US3 - Position detail shows source currency P&L
- [ ] T062 [P] Manual testing: US4 - Chart shows monthly data points
- [ ] T063 [P] Manual testing: US5 - Benchmark shows Yahoo Total Return
- [ ] T064 Manual testing: US6 - User stays logged in for 2+ hours

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-8)**: All depend on Foundational phase completion
  - US1 and US2 (P1): Highest priority, complete first
  - US3 and US4 (P2): Medium priority
  - US5 and US6 (P3): Lower priority, can be done last
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational - No dependencies on other stories
- **US2 (P1)**: Can start after Foundational - No dependencies on other stories
- **US3 (P2)**: Can start after Foundational - Independent of US1/US2
- **US4 (P2)**: Can start after Foundational - Independent of other stories
- **US5 (P3)**: Can start after Foundational - Independent of other stories
- **US6 (P3)**: Can start after Foundational - Independent of other stories (can run in parallel with any phase)

### Parallel Opportunities

- T001/T002: Entity creation can run in parallel
- T009-T013: DTO creation and tests can run in parallel
- T019/T022/T023: Frontend work can run in parallel
- T029/T034/T036/T043/T045/T046: Tests and API methods marked [P]
- US6 (T048-T054): Entirely independent, can run parallel with any user story

---

## Summary

| Phase | Story | Priority | Tasks | Description |
|-------|-------|----------|-------|-------------|
| 1 | - | P1 | T001-T006 | Setup: Entities & Migrations |
| 2 | - | P1 | T007-T014 | Foundational: Calculator & DTOs |
| 3 | US1 | P1 | T015-T021 | Simple Return Display |
| 4 | US2 | P1 | T022-T026 | Currency Toggle |
| 5 | US3 | P2 | T027-T030 | Source Currency P&L |
| 6 | US4 | P2 | T031-T038 | Monthly Net Worth Chart |
| 7 | US5 | P3 | T039-T046 | Yahoo Benchmark Total Return |
| 8 | US6 | P3 | T047-T053 | Token Refresh |
| 9 | - | P1 | T054-T064 | Polish & Validation |

**Total Tasks**: 64
**Critical Path**: Phase 1 → Phase 2 → Phase 3/4 (P1 parallel) → Phase 5/6 (P2 parallel) → Phase 7/8 (P3 parallel) → Phase 9

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
