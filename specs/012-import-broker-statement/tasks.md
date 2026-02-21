# Tasks: Unified Broker Statement Import

**Input**: Design documents from `/specs/012-import-broker-statement/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Include test tasks because spec success criteria require acceptance verification and regression coverage.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared contracts/types for new import workflow

- [x] T001 Add stock import API request/response client types in `frontend/src/types/index.ts`
- [x] T002 Add stock import API client methods in `frontend/src/services/api.ts`
- [x] T003 [P] Add backend DTO skeletons for stock import preview/execute, including canonical `confirmedTradeSide` and balance-decision fields, in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [x] T004 [P] Add backend response DTOs for stock import diagnostics in `backend/src/InvestmentTracker.Application/DTOs/StockImportDtos.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core import infrastructure required before any user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 Create TW security mapping entity in `backend/src/InvestmentTracker.Domain/Entities/TwSecurityMapping.cs`
- [x] T006 Configure TW security mapping table/indexes in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/TwSecurityMappingConfiguration.cs`
- [x] T007 Register mapping DbSet in `backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs`
- [x] T008 Add EF migration for TW security mapping table in `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [x] T009 [P] Add repository interface for TW security mapping lookup/upsert in `backend/src/InvestmentTracker.Domain/Interfaces/ITwSecurityMappingRepository.cs`
- [x] T010 [P] Implement TW security mapping repository in `backend/src/InvestmentTracker.Infrastructure/Repositories/TwSecurityMappingRepository.cs`
- [x] T011 Implement on-demand TWSE ISIN source synchronization service in `backend/src/InvestmentTracker.Infrastructure/Services/TwseSymbolMappingService.cs`
- [x] T012 Wire DI registrations for mapping repository/service in `backend/src/InvestmentTracker.Infrastructure/DependencyInjection.cs`
- [x] T013 Add API endpoint for on-demand symbol sync in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Import broker statement from existing entry (Priority: P1) 🎯 MVP

**Goal**: User can upload broker statement CSV from existing stock import entry, get format detection + preview, resolve symbols, and execute import.

**Independent Test**: Upload `證券app匯出範例.csv`, verify broker format detection/override, preview normalized rows, resolve unresolved symbol rows, and execute with row-level summary.

### Tests for User Story 1

- [x] T014 [P] [US1] Add backend preview/execute contract tests (including preview-to-created value consistency checks for date/quantity/price/fees) in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T015 [P] [US1] Add TWSE sync endpoint tests (including per-unresolved-row sync-attempt assertions) in `backend/tests/InvestmentTracker.API.Tests/Controllers/MarketDataControllerTwseSyncTests.cs`
- [x] T016 [P] [US1] Add frontend import preview flow tests for broker format detection/override, ambiguous-side per-row confirmation, and stable row ordering in `frontend/src/test/stock-import.broker-preview.test.tsx`

### Implementation for User Story 1

- [x] T017 [US1] Implement broker statement parser and format detector in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportParser.cs`
- [x] T018 [US1] Implement symbol resolution workflow (local lookup -> on-demand sync -> unresolved list) in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportSymbolResolver.cs`
- [x] T019 [US1] Implement stock import preview use case in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/PreviewStockImportUseCase.cs`
- [x] T020 [US1] Implement stock import execute use case with pre-execution blocking for unresolved ambiguous-side confirmations in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [x] T021 [US1] Add stock import preview/execute API endpoints in `backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs`
- [x] T022 [US1] Extend stock import button flow for format detection/override and unresolved-row remediation in `frontend/src/components/import/StockImportButton.tsx`
- [x] T023 [US1] Extend reusable import modal view states for unresolved symbol input rows in original row order and manual ticker entry in `frontend/src/components/import/CSVImportModal.tsx`
- [x] T024 [P] [US1] Add broker-statement field aliases, normalization helpers, and row-order-preserving metadata in `frontend/src/utils/csvParser.ts`

**Checkpoint**: User Story 1 should be fully functional and independently testable.

---

## Phase 4: User Story 2 - Resolve insufficient balance during import (Priority: P1)

**Goal**: Buy rows with insufficient balance require explicit Margin/Top-up decisions, matching manual transaction behavior.

**Independent Test**: Import broker rows with buy shortfalls; verify unresolved rows require balance action, Top-up type validation applies, and execution blocks unresolved rows.

### Tests for User Story 2

- [x] T025 [P] [US2] Add backend use-case tests for shortfall decision handling, unresolved-decision execution blocking, and row-level error codes/messages in `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/ExecuteStockImportBalanceActionTests.cs`
- [x] T026 [P] [US2] Add frontend interaction tests for global/per-row balance actions in `frontend/src/test/stock-import.balance-action.test.tsx`

### Implementation for User Story 2

- [x] T027 [US2] Add import execution orchestration that applies `BalanceAction`/`TopUpTransactionType` per row and blocks rows with unresolved decisions using explicit row-level failure reasons in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [x] T028 [US2] Reuse and adapt manual transaction shortfall checks for import rows in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs`
- [x] T029 [US2] Extend import execute request DTO for default + per-row balance decisions in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [x] T030 [US2] Add UI for global default and per-row override of balance action in `frontend/src/components/import/StockImportButton.tsx`
- [x] T031 [US2] Add Top-up transaction type selection and validation messaging in `frontend/src/components/import/CSVImportModal.tsx`

**Checkpoint**: User Story 2 should be independently functional and testable.

---

## Phase 5: User Story 3 - Preserve existing CSV import behavior (Priority: P2)

**Goal**: Legacy stock CSV import remains supported with no regression from unified import changes.

**Independent Test**: Import known-valid legacy CSV in same entry and confirm preview/execution outcomes remain equivalent to current behavior.

### Tests for User Story 3

- [x] T032 [P] [US3] Add backend regression tests for legacy CSV preview/execute path in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsLegacyImportRegressionTests.cs`
- [x] T033 [P] [US3] Add frontend legacy import regression test in `frontend/src/test/stock-import.legacy-regression.test.tsx`

### Implementation for User Story 3

- [x] T034 [US3] Implement legacy-vs-broker format routing with manual override precedence in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportParser.cs`
- [x] T035 [US3] Preserve existing row-level result mapping shape for legacy imports in `frontend/src/components/import/StockImportButton.tsx`
- [x] T036 [US3] Update import UX copy for dual-format support without changing legacy workflow defaults in `frontend/src/components/import/CSVImportModal.tsx`

**Checkpoint**: User Stories 1-3 should all work independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, observability, docs, and end-to-end validation

- [x] T037 [P] Add structured logging and error codes for import preview/execute and TWSE ISIN source sync failures in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/`
- [x] T038 [P] Add OpenAPI annotations/examples for new import and sync endpoints in `backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs` and `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [x] T039 Update quickstart verification notes after implementation in `specs/012-import-broker-statement/quickstart.md`
- [x] T040 Execute quickstart scenarios and record verification evidence by updating `specs/012-import-broker-statement/quickstart.md` *(補充：新增新帳號→匯入證券app範例→績效驗證情境，並以自動化測試覆蓋 preview/execute/performance 契約)*
- [x] T041 [P] Run 500-row broker preview performance benchmark and assert <=3s target in `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/PreviewStockImportPerformanceTests.cs`
- [x] T042 Clarify net-positive holdings semantics (position cards show only `totalShares > 0`, not silent import-row drops) in `frontend/src/pages/Portfolio.tsx` and `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T043 [P] Add frontend disclosure/behavior regression coverage for net-positive holdings rendering in `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`
- [x] T044 Fix current-year performance loading fallback (YTD unavailable -> benchmark returns fallback) to prevent persistent spinner in `frontend/src/pages/Performance.tsx`
- [x] T045 [P] Add fallback loading-unblock regression test for current-year benchmark flow in `frontend/src/test/performance.metrics-binding.test.tsx`
- [x] T046 Deduplicate repeated `missingPrices` tickers before quote fetch to reduce duplicate external calls in `frontend/src/pages/Performance.tsx`
- [x] T047 [P] Add quote-fetch dedupe and cache-key compatibility regression tests (market-aware key + legacy fallback) in `frontend/src/test/performance.metrics-binding.test.tsx` and `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`
- [x] T048 Harden backend sample CSV fixture resolution for path-independent test execution in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T049 [P] Ensure broker sample fixture is copied into test output directory for fixture path independence in `backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj`
- [x] T050 Update reliability-cycle verification evidence and test-infra scope note (no Playwright; covered by API + frontend integration tests) in `specs/012-import-broker-statement/quickstart.md` and `frontend/src/test/stock-import.broker-preview.test.tsx`
- [x] T051 Add import baseline request contracts (`baselineDate`, opening positions/cost, opening cash/ledger balance) in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [x] T052 [P] Add import session baseline snapshot DTO scaffold in `backend/src/InvestmentTracker.Application/DTOs/StockImportDtos.cs`
- [x] T053 Persist baseline scaffold into preview session snapshot in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/PreviewStockImportUseCase.cs`
- [x] T054 [P] Add performance coverage/baseline signal fields to yearly response DTO in `backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs`
- [x] T059 [P] Add API contract assertions for baseline/coverage signal fields in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T060 Align import UI/API contract consistency (types + StockImportButton + CSVImportModal) in `frontend/src/types/index.ts`, `frontend/src/components/import/StockImportButton.tsx`, and `frontend/src/components/import/CSVImportModal.tsx`
- [x] T061 Align performance reliability/coverage behavior and metric-binding consistency in `frontend/src/pages/Performance.tsx`
- [x] T062 Align import regression tests in `frontend/src/test/stock-import.broker-preview.test.tsx`, `frontend/src/test/stock-import.balance-action.test.tsx`, and `frontend/src/test/stock-import.legacy-regression.test.tsx`
- [x] T063 Align performance regression tests in `frontend/src/test/performance.metrics-binding.test.tsx` and `frontend/src/test/useHistoricalPerformance.test.ts`
- [x] T064 Record Group E QA verification execution log in `specs/012-import-broker-statement/quickstart.md`
- [x] T065 Resolve Group E code-review findings (market-aware + legacy quote cache write and conditional TopUp gating) in `frontend/src/pages/Performance.tsx` and `frontend/src/components/import/StockImportButton.tsx`
- [x] T066 Update Group E quickstart evidence and traceability in `specs/012-import-broker-statement/quickstart.md`
- [x] T067 Update Speckit tasks checklist for Group E completion in `specs/012-import-broker-statement/tasks.md`
- [x] T068 Fix execute ordering to trade-date-first with deterministic tie-breaker (sell before buy on same date) in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [x] T069 Add application regression tests for reverse-chronological and same-day buy-first scenarios without over-topup in `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/ExecuteStockImportBalanceActionTests.cs`
- [x] T070 Add API regression tests for reverse-chronological and same-day buy-first scenarios without over-topup in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T071 Record Group F backend QA verification evidence in `specs/012-import-broker-statement/quickstart.md`
- [x] T072 Resolve Group F code-review findings (same-day ordering risk + duplicate rowNumber guard) in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs` and `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/ExecuteStockImportBalanceActionTests.cs`
- [x] T073 Update Speckit checklist and quickstart traceability for Group F in `specs/012-import-broker-statement/tasks.md` and `specs/012-import-broker-statement/quickstart.md`
- [x] T074 Add UI-equivalent import→performance regression that deterministically reproduces extreme 2025 Modified Dietz with no opening baseline and late top-up in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T075 Add service-level diagnostics assertions for MD anomaly factors (`coverageDays`, `hasOpeningBaseline`, `usesPartialHistoryAssumption`, `xirrReliability`, denominator lock) in `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`
- [x] T076 Enforce Yahoo-first non-realtime historical price lookup in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [x] T077 Restrict Stooq fallback to Yahoo-failure + US/UK markets in `backend/src/InvestmentTracker.Infrastructure/Services/HistoricalYearEndDataService.cs` and `backend/src/InvestmentTracker.Infrastructure/Services/MonthlySnapshotService.cs`
- [x] T078 Add backend regression tests for historical source priority and fallback scope in `backend/tests/InvestmentTracker.Infrastructure.Tests/Services/HistoricalYearEndDataServiceTests.cs` and `backend/tests/InvestmentTracker.API.Tests/Controllers/MarketDataControllerHistoricalPriceTests.cs`
- [x] T079 Implement explicit XIRR UI state split (calculating vs low-confidence vs unavailable) in `frontend/src/pages/Dashboard.tsx` and `frontend/src/pages/Performance.tsx`
- [x] T080 Add frontend regression tests for XIRR state split in `frontend/src/test/dashboard.aggregate-fixed.test.tsx` and `frontend/src/test/performance.metrics-binding.test.tsx`
- [x] T081 Record Group G root-cause and source-policy verification evidence in `specs/012-import-broker-statement/quickstart.md`
- [x] T082 Update Speckit checklist for Group G completion in `specs/012-import-broker-statement/tasks.md`
- [x] T083 Update quickstart traceability matrix for Group G in `specs/012-import-broker-statement/quickstart.md`
- [x] T084 Disable single-year XIRR primary value rendering in `frontend/src/pages/Performance.tsx`
- [x] T085 Add frontend regression assertions for single-year XIRR disablement and degrade hint behavior in `frontend/src/test/performance.metrics-binding.test.tsx`
- [x] T086 Unify dashboard XIRR status copy and visual contrast in `frontend/src/pages/Dashboard.tsx`
- [x] T087 Replace portfolio XIRR `-` placeholder with explicit status copy in `frontend/src/components/portfolio/PerformanceMetrics.tsx`
- [x] T088 Add dashboard/portfolio XIRR state regression tests in `frontend/src/test/dashboard.aggregate-fixed.test.tsx` and `frontend/src/test/portfolio.performance-metrics.test.tsx`
- [x] T089 Add annual return display degrade contract fields in `backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs`
- [x] T090 Implement low-confidence annual return degrade signal in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [x] T091 Apply aggregate annual return degrade signal in `backend/src/InvestmentTracker.Application/UseCases/Performance/CalculateAggregateYearPerformanceUseCase.cs`
- [x] T092 Add backend branch regression tests for degrade reasons in `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs` and `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs`
- [x] T093 Add API regression assertions for annual degrade signal contract in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T094 Integrate degrade reason warning rendering in `frontend/src/pages/Performance.tsx` and `frontend/src/types/index.ts`
- [x] T095 Add import-to-performance degraded-summary regression coverage in `frontend/src/test/stock-import.broker-preview.test.tsx`
- [x] T096 Update Speckit checklist for Group H completion in `specs/012-import-broker-statement/tasks.md`
- [x] T097 Record Group H QA evidence and traceability in `specs/012-import-broker-statement/quickstart.md`
- [x] T098 Remove the single-year XIRR disablement sentence from annual performance card in `frontend/src/pages/Performance.tsx`
- [x] T099 Update low-confidence annual messaging to explicitly cover MD/TWR/XIRR indicator confidence in `frontend/src/pages/Performance.tsx`
- [x] T100 Change dashboard unavailable copy to `資料不足不顯示` and add info-tooltip reason in `frontend/src/pages/Dashboard.tsx`
- [x] T101 Change portfolio unavailable copy to `資料不足不顯示` and add info-tooltip reason in `frontend/src/components/portfolio/PerformanceMetrics.tsx`
- [x] T102 Move holdings net-share visibility note into info-tooltip in `frontend/src/pages/Portfolio.tsx`
- [x] T103 Update baseline input labels/placeholders (`可空`) and remove `(可空)` suffix in `frontend/src/components/import/CSVImportModal.tsx`
- [x] T104 Rename `期初持倉（可多筆）` heading to `期初持倉` in `frontend/src/components/import/CSVImportModal.tsx`
- [x] T105 Replace `賣先買後預設處理方式` with user-friendly wording and tooltip in `frontend/src/components/import/StockImportButton.tsx` and `frontend/src/components/import/CSVImportModal.tsx`
- [x] T106 Reduce `賣先買後處理` preview column width and widen import modal to reduce horizontal scrolling in `frontend/src/components/import/CSVImportModal.tsx`
- [x] T107 Add frontend regressions for updated copy/tooltips/layout in `frontend/src/test/performance.metrics-binding.test.tsx`, `frontend/src/test/dashboard.aggregate-fixed.test.tsx`, `frontend/src/test/portfolio.performance-metrics.test.tsx`, `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`, and `frontend/src/test/stock-import.balance-action.test.tsx`
- [x] T108 Add backend regression assertions with concrete reproducible 2025 MD extreme-value data points in `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`, `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs`, and `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T109 Record Group I verification evidence and concrete 2025 MD explanation in `specs/012-import-broker-statement/quickstart.md`
- [x] T110 Update Speckit checklist for Group I completion in `specs/012-import-broker-statement/tasks.md`
- [x] T111 Scope no-external-cash-flow fallback to TWR snapshot event selection only (without mutating MD/NetContributions cash-flow path) in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [x] T112 [P] Harden TWR calculator guardrails for negative valuations and cross-zero transitions in `backend/src/InvestmentTracker.Domain/Services/ReturnCalculator.cs`
- [x] T113 [P] Add domain regression coverage for negative/cross-zero TWR guard behavior in `backend/tests/InvestmentTracker.Domain.Tests/Services/ReturnCalculatorTests.cs`
- [x] T114 Add application regression coverage proving no-external-cash-flow fallback affects TWR only while keeping MD/NetContributions unchanged in `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`
- [x] T115 Add API regression assertions for reproducible import-to-year-performance comparison and data-path consistency in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T116 Add operational logging for TWR-only fallback activation under missing explicit external cash-flow events in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [x] T117 Add annual performance DTO contract fields for recent large inflow warning signal in `backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs`
- [x] T118 Implement recent-large-inflow warning rule (last 10% period window and inflow >50% of period total assets) in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [x] T119 Propagate recent-large-inflow warning signal to aggregate yearly response in `backend/src/InvestmentTracker.Application/UseCases/Performance/CalculateAggregateYearPerformanceUseCase.cs`
- [x] T120 [P] Add service-level regression tests for warning threshold boundaries and YTD actual-day weighting behavior in `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`
- [x] T121 [P] Add aggregate use-case regression tests for warning propagation and current-year actual-day Modified Dietz weighting in `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs`
- [x] T122 Add API contract regression assertions for warning signal/message fields and reliability signal compatibility in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [x] T123 Update annual performance copy to scope low-confidence wording to MD only and render recent-large-inflow warning banner in `frontend/src/pages/Performance.tsx`
- [x] T124 Align Dashboard and Portfolio XIRR unavailable copy/tooltip behavior with explicit state wording in `frontend/src/pages/Dashboard.tsx` and `frontend/src/components/portfolio/PerformanceMetrics.tsx`
- [x] T125 Improve tooltip-driven guidance wording and keyboard accessibility for portfolio/import interactions in `frontend/src/pages/Portfolio.tsx`, `frontend/src/components/import/StockImportButton.tsx`, and `frontend/src/components/import/CSVImportModal.tsx`
- [x] T126 Extend frontend yearly performance type contract with `hasRecentLargeInflowWarning` in `frontend/src/types/index.ts`
- [x] T127 [P] Add frontend regression coverage for performance copy updates, warning rendering, and tooltip a11y semantics in `frontend/src/test/performance.metrics-binding.test.tsx`, `frontend/src/test/dashboard.aggregate-fixed.test.tsx`, and `frontend/src/test/portfolio.performance-metrics.test.tsx`
- [x] T128 [P] Add frontend regression coverage for import tooltip a11y semantics and related copy updates in `frontend/src/test/stock-import.balance-action.test.tsx`, `frontend/src/test/stock-import.broker-preview.test.tsx`, and `frontend/src/test/useHistoricalPerformance.test.ts`
- [x] T129 Add `MarketValueAtImport` and `HistoricalTotalCost` fields to `StockTransaction` and update EF mapping/migration in `backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs`, `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/StockTransactionConfiguration.cs`, and `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [x] T130 Extend stock import opening-position contracts with optional `HistoricalTotalCost` in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs` and `backend/src/InvestmentTracker.Application/DTOs/StockImportDtos.cs`
- [x] T131 Align baseline snapshot/payload contract for `HistoricalTotalCost` in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/PreviewStockImportUseCase.cs`, `frontend/src/types/index.ts`, and `frontend/src/components/import/StockImportButton.tsx`
- [x] T132 Inject historical pricing dependency into stock import execution flow in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [x] T133 Implement mark-to-market seeded opening adjustment with paired `CurrencyTransactionType.InitialBalance` booking and cross-currency baseline FX handling in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1; blocks all user stories
- **Phase 3 (US1)**: Depends on Phase 2; establishes MVP
- **Phase 4 (US2)**: Depends on Phase 3 preview/execute baseline
- **Phase 5 (US3)**: Depends on Phase 3 baseline; can run after Phase 4 backend DTO stabilization
- **Phase 6 (Polish)**: Depends on completion of desired stories

### User Story Dependencies

- **US1 (P1)**: No story dependency once foundation is ready
- **US2 (P1)**: Depends on US1 import preview/execute pipeline
- **US3 (P2)**: Depends on US1 dual-format import framework; independent of US2 business behavior

### Parallel Opportunities

- Phase 1: T003 and T004 parallel
- Phase 2: T009 and T010 parallel after entity/config scaffolding
- US1: T014/T015/T016 parallel tests; T024 parallel with T022/T023
- US2: T025 and T026 parallel tests
- US3: T032 and T033 parallel tests
- Phase 6: T037, T038, and T041 parallel

---

## Parallel Example: User Story 1

```bash
# Parallel test tasks
T014 backend import contract tests
T015 backend TWSE sync endpoint tests
T016 frontend broker preview tests

# Parallel implementation tasks after API shape is stable
T022 frontend stock import flow updates
T023 frontend modal unresolved-row UX
T024 csv parser alias helpers
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 Setup
2. Complete Phase 2 Foundational
3. Complete Phase 3 (US1)
4. Validate independent test for US1 with broker sample file

### Incremental Delivery

1. Deliver US1 (broker import preview/execute + unresolved symbol remediation)
2. Deliver US2 (insufficient-balance decision parity with manual create)
3. Deliver US3 (legacy CSV regression hardening)
4. Execute Phase 6 polish and quickstart evidence

### Team Parallel Strategy

1. Foundation split: DB/migration lane + service/API lane
2. After foundation:
   - Engineer A: backend import parser/use cases
   - Engineer B: frontend import UX and state transitions
   - Engineer C: tests and regression suite

---

## Notes

- `[P]` tasks indicate non-conflicting parallel work.
- Story labels `[US1]`, `[US2]`, `[US3]` ensure traceability to spec user stories.
- Each user story has explicit independent test criteria.
- Task descriptions include exact file paths for direct execution.
