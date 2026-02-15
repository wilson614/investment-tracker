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
- [x] T013 Add API endpoint for on-demand symbol sync in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Import broker statement from existing entry (Priority: P1) 🎯 MVP

**Goal**: User can upload broker statement CSV from existing stock import entry, get format detection + preview, resolve symbols, and execute import.

**Independent Test**: Upload `證券app匯出範例.csv`, verify broker format detection/override, preview normalized rows, resolve unresolved symbol rows, and execute with row-level summary.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add backend preview/execute contract tests (including preview-to-created value consistency checks for date/quantity/price/fees) in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
- [ ] T015 [P] [US1] Add TWSE sync endpoint tests (including per-unresolved-row sync-attempt assertions) in `backend/tests/InvestmentTracker.API.Tests/Controllers/MarketDataControllerTwseSyncTests.cs`
- [ ] T016 [P] [US1] Add frontend import preview flow tests for broker format detection/override, ambiguous-side per-row confirmation, and stable row ordering in `frontend/src/test/stock-import.broker-preview.test.tsx`

### Implementation for User Story 1

- [ ] T017 [US1] Implement broker statement parser and format detector in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportParser.cs`
- [ ] T018 [US1] Implement symbol resolution workflow (local lookup -> on-demand sync -> unresolved list) in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportSymbolResolver.cs`
- [ ] T019 [US1] Implement stock import preview use case in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/PreviewStockImportUseCase.cs`
- [ ] T020 [US1] Implement stock import execute use case with pre-execution blocking for unresolved ambiguous-side confirmations in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [ ] T021 [US1] Add stock import preview/execute API endpoints in `backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs`
- [ ] T022 [US1] Extend stock import button flow for format detection/override and unresolved-row remediation in `frontend/src/components/import/StockImportButton.tsx`
- [ ] T023 [US1] Extend reusable import modal view states for unresolved symbol input rows in original row order and manual ticker entry in `frontend/src/components/import/CSVImportModal.tsx`
- [ ] T024 [P] [US1] Add broker-statement field aliases, normalization helpers, and row-order-preserving metadata in `frontend/src/utils/csvParser.ts`

**Checkpoint**: User Story 1 should be fully functional and independently testable.

---

## Phase 4: User Story 2 - Resolve insufficient balance during import (Priority: P1)

**Goal**: Buy rows with insufficient balance require explicit Margin/Top-up decisions, matching manual transaction behavior.

**Independent Test**: Import broker rows with buy shortfalls; verify unresolved rows require balance action, Top-up type validation applies, and execution blocks unresolved rows.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add backend use-case tests for shortfall decision handling, unresolved-decision execution blocking, and row-level error codes/messages in `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/ExecuteStockImportBalanceActionTests.cs`
- [ ] T026 [P] [US2] Add frontend interaction tests for global/per-row balance actions in `frontend/src/test/stock-import.balance-action.test.tsx`

### Implementation for User Story 2

- [ ] T027 [US2] Add import execution orchestration that applies `BalanceAction`/`TopUpTransactionType` per row and blocks rows with unresolved decisions using explicit row-level failure reasons in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
- [ ] T028 [US2] Reuse and adapt manual transaction shortfall checks for import rows in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs`
- [ ] T029 [US2] Extend import execute request DTO for default + per-row balance decisions in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`
- [ ] T030 [US2] Add UI for global default and per-row override of balance action in `frontend/src/components/import/StockImportButton.tsx`
- [ ] T031 [US2] Add Top-up transaction type selection and validation messaging in `frontend/src/components/import/CSVImportModal.tsx`

**Checkpoint**: User Story 2 should be independently functional and testable.

---

## Phase 5: User Story 3 - Preserve existing CSV import behavior (Priority: P2)

**Goal**: Legacy stock CSV import remains supported with no regression from unified import changes.

**Independent Test**: Import known-valid legacy CSV in same entry and confirm preview/execution outcomes remain equivalent to current behavior.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add backend regression tests for legacy CSV preview/execute path in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsLegacyImportRegressionTests.cs`
- [ ] T033 [P] [US3] Add frontend legacy import regression test in `frontend/src/test/stock-import.legacy-regression.test.tsx`

### Implementation for User Story 3

- [ ] T034 [US3] Implement legacy-vs-broker format routing with manual override precedence in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockImportParser.cs`
- [ ] T035 [US3] Preserve existing row-level result mapping shape for legacy imports in `frontend/src/components/import/StockImportButton.tsx`
- [ ] T036 [US3] Update import UX copy for dual-format support without changing legacy workflow defaults in `frontend/src/components/import/CSVImportModal.tsx`

**Checkpoint**: User Stories 1-3 should all work independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, observability, docs, and end-to-end validation

- [ ] T037 [P] Add structured logging and error codes for import preview/execute and TWSE ISIN source sync failures in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/`
- [ ] T038 [P] Add OpenAPI annotations/examples for new import and sync endpoints in `backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs` and `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [ ] T039 Update quickstart verification notes after implementation in `specs/012-import-broker-statement/quickstart.md`
- [ ] T040 Execute quickstart scenarios and record verification evidence by updating `specs/012-import-broker-statement/quickstart.md`
- [ ] T041 [P] Run 500-row broker preview performance benchmark and assert <=3s target in `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/PreviewStockImportPerformanceTests.cs`

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
