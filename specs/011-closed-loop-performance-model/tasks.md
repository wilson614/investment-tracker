# Tasks: Closed-Loop Performance Model & Transaction Type Redesign

**Input**: Design documents from `/specs/011-closed-loop-performance-model/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: This spec contains explicit independent test criteria and measurable outcomes, so story-level tests are included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature scaffolding and reusable contract/validation artifacts.

- [ ] T001 Create feature task baseline and traceability notes in /workspaces/InvestmentTracker/specs/011-closed-loop-performance-model/tasks.md
- [ ] T002 Define transaction-category policy matrix document for implementation reference in /workspaces/InvestmentTracker/specs/011-closed-loop-performance-model/contracts/api-contracts.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core backend/DTO foundations required by all user stories.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Rename and redefine currency transaction enum contract in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Domain/Enums/CurrencyTransactionType.cs
- [ ] T004 [P] Align currency transaction DTO/request enum usage with redesigned categories in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/DTOs/CurrencyLedgerDtos.cs
- [ ] T005 [P] Update domain entity-level transaction invariants for redesigned categories in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Domain/Entities/CurrencyTransaction.cs
- [x] T006 Implement shared ledger-currency/type validation policy helper for create/update/import flows in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/CurrencyTransactionTypePolicy.cs
- [x] T007 Update stock-linked ledger transaction category mapping to dedicated internal categories in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockTransactionLinking.cs

**Checkpoint**: Foundation ready — user story implementation can begin.

---

## Phase 3: User Story 1 - Unified Transaction Semantics and Validation (Priority: P1) 🎯 MVP

**Goal**: Ensure transaction categories are unambiguous and validated consistently across backend create/update and CSV import.

**Independent Test**: Submit create/update requests and CSV rows for both TWD/non-TWD ledgers; invalid combinations must be rejected consistently and valid combinations accepted.

### Tests for User Story 1

- [ ] T008 [P] [US1] Add/adjust backend validation matrix tests for TWD vs non-TWD category rules in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/UseCases/CurrencyLedger/TwdLedgerTransactionTypesTests.cs
- [ ] T009 [P] [US1] Add create/update request validation regression tests for category+ledger combinations in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/UseCases/CurrencyLedger/TwdLedgerTransactionTypesTests.cs
- [ ] T010 [P] [US1] Add API integration tests for invalid/valid currency transaction create/update requests in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs
- [ ] T011 [P] [US1] Add CSV import rejection integration tests asserting full error-set reporting for all invalid rows/fields in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs
- [ ] T043 [P] [US1] Add legacy enum payload rejection tests to ensure deprecated enum names are rejected by create/update APIs in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs
- [ ] T044 [P] [US1] Add CSV diagnostics schema fixed-field assertion tests for row/field/value/guidance keys in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs

### Implementation for User Story 1

- [ ] T012 [US1] Enforce strict ledger-currency/type policy in create flow in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/CreateCurrencyTransactionUseCase.cs
- [ ] T013 [US1] Enforce strict ledger-currency/type policy in update flow in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/UpdateCurrencyTransactionUseCase.cs
- [ ] T014 [US1] Update create request validator for redesigned category semantics and required fields in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/Validators/CreateCurrencyTransactionRequestValidator.cs
- [ ] T015 [US1] Update update request validator to match create-rule strictness in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/Validators/UpdateCurrencyTransactionRequestValidator.cs
- [ ] T016 [US1] Add atomic currency CSV import endpoint with all-or-nothing behavior in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.API/Controllers/CurrencyTransactionsController.cs
- [ ] T017 [US1] Implement currency CSV parsing/validation/diagnostics application service for full error set responses in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/ImportCurrencyTransactionsUseCase.cs
- [ ] T018 [US1] Align frontend CSV import flow to new backend atomic import contract in /workspaces/InvestmentTracker/frontend/src/components/import/CurrencyImportButton.tsx
- [ ] T019 [US1] Update frontend category labels and parser mappings to accept redesigned enum names only (no deprecated enum mappings) in /workspaces/InvestmentTracker/frontend/src/components/currency/CurrencyTransactionForm.tsx
- [ ] T020 [P] [US1] Update frontend category labels and options in stock transaction supplement flow in /workspaces/InvestmentTracker/frontend/src/components/transactions/TransactionForm.tsx
- [ ] T021 [P] [US1] Update transaction type labels in currency detail display in /workspaces/InvestmentTracker/frontend/src/pages/CurrencyDetail.tsx
- [ ] T022 [P] [US1] Update transaction type export labels for CSV consistency in /workspaces/InvestmentTracker/frontend/src/services/csvExport.ts
- [ ] T023 [US1] Update shared transaction type constants for redesigned enum names in /workspaces/InvestmentTracker/frontend/src/types/index.ts

**Checkpoint**: User Story 1 should be fully functional and independently testable.

---

## Phase 4: User Story 2 - Closed-Loop Valuation and Return Calculation (Priority: P1)

**Goal**: Make annual performance metrics use strict closed-loop valuation and explicit external-only CF policy.

**Independent Test**: Run annual performance fixtures with negative ledger and mixed internal/external events; verify baseline and CF inclusion match spec.

### Tests for User Story 2

- [ ] T024 [P] [US2] Add/adjust ReturnCashFlowStrategy regression tests for explicit external-only inclusion policy in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Domain.Tests/Services/ReturnCalculatorTests.cs
- [ ] T045 [P] [US2] Add internal FX effects exclusion tests to verify internal FX transfer events are excluded from MD/TWR external CF inputs in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs
- [ ] T025 [P] [US2] Add annual performance service regression tests for unified closed-loop baseline in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs
- [ ] T026 [P] [US2] Add snapshot service regression tests confirming negative ledger is not floored in valuation in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Infrastructure.Tests/Services/TransactionPortfolioSnapshotServiceTests.cs
- [ ] T027 [P] [US2] Add aggregate performance integration regression tests for MD/TWR baseline parity in /workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/Integration/PortfoliosControllerTests.cs

### Implementation for User Story 2

- [ ] T028 [US2] Refactor return cash-flow strategy to include explicit external categories only and explicitly classify/exclude internal FX effects per new policy in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Domain/Services/ReturnCashFlowStrategy.cs
- [ ] T029 [US2] Update annual performance calculation pipeline to use closed-loop baseline for MD and TWR consistently in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs
- [ ] T030 [US2] Remove non-positive ledger floor logic from snapshot valuation path in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Infrastructure/Services/TransactionPortfolioSnapshotService.cs
- [ ] T031 [US2] Verify and align stock-linked transaction generation with internal-event exclusion policy in /workspaces/InvestmentTracker/backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockTransactionLinking.cs

**Checkpoint**: User Stories 1 and 2 should both be independently functional.

---

## Phase 5: User Story 3 - UX Copy and User Interpretation Alignment (Priority: P2)

**Goal**: Ensure MD/TWR explanatory wording and transaction naming are consistent with redesigned model language.

**Independent Test**: Open performance help UI and verify required wording appears in all relevant views with no deprecated text.

### Tests for User Story 3

- [ ] T032 [P] [US3] Update frontend metric-binding tests for revised MD/TWR wording expectations in /workspaces/InvestmentTracker/frontend/src/test/performance.metrics-binding.test.tsx
- [ ] T033 [P] [US3] Add/update frontend tests for transaction type display naming consistency in /workspaces/InvestmentTracker/frontend/src/test/currency.transaction-type-display.test.tsx

### Implementation for User Story 3

- [ ] T034 [US3] Update MD/TWR helper copy to required final wording in /workspaces/InvestmentTracker/frontend/src/pages/Performance.tsx
- [ ] T035 [US3] Remove deprecated transaction/help wording variants across form/detail/export surfaces in /workspaces/InvestmentTracker/frontend/src/components/currency/CurrencyTransactionForm.tsx
- [ ] T036 [P] [US3] Remove deprecated transaction/help wording variants across form/detail/export surfaces in /workspaces/InvestmentTracker/frontend/src/components/transactions/TransactionForm.tsx
- [ ] T037 [P] [US3] Remove deprecated transaction/help wording variants across form/detail/export surfaces in /workspaces/InvestmentTracker/frontend/src/pages/CurrencyDetail.tsx
- [ ] T038 [P] [US3] Remove deprecated transaction/help wording variants across form/detail/export surfaces in /workspaces/InvestmentTracker/frontend/src/services/csvExport.ts

**Checkpoint**: All user stories should now be independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency and verification across stories.

- [ ] T039 [P] Run backend regression suite for impacted domains in /workspaces/InvestmentTracker/backend/InvestmentTracker.sln
- [ ] T040 [P] Run frontend type-check and test suite for impacted pages/components in /workspaces/InvestmentTracker/frontend/package.json
- [ ] T041 Validate quickstart execution and update completion checkboxes in /workspaces/InvestmentTracker/specs/011-closed-loop-performance-model/quickstart.md
- [x] T042 Perform cross-story consistency pass on contracts/spec/tasks wording alignment in /workspaces/InvestmentTracker/specs/011-closed-loop-performance-model/contracts/api-contracts.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies
- **Phase 2 (Foundational)**: depends on Phase 1; blocks all user stories
- **Phase 3 (US1)**: depends on Phase 2 completion
- **Phase 4 (US2)**: depends on Phase 2 completion; can run in parallel with late US1 frontend-only tasks if backend ownership avoids file conflicts
- **Phase 5 (US3)**: depends on US1 label changes and can proceed after US1 core implementation
- **Phase 6 (Polish)**: depends on all target user stories complete

### User Story Dependencies

- **US1 (P1)**: first MVP slice; no dependency on other stories
- **US2 (P1)**: depends on foundational enum/policy scaffolding from Phase 2, but independently testable
- **US3 (P2)**: depends on final naming outcomes from US1/US2 to avoid wording mismatch

### Within Each User Story

- Tests should be prepared before/alongside implementation and must validate final behavior.
- Backend contract/validation tasks precede frontend integration tasks.
- Core logic changes precede broad cleanup/polish.

### Parallel Opportunities

- Phase 2 tasks T004/T005 can run in parallel after T003.
- US1 test tasks T008–T011 and T043–T044 run in parallel.
- US1 frontend label tasks T020–T022 run in parallel after backend enum contract stabilizes.
- US2 test tasks T024–T027 and T045 run in parallel.
- US3 cleanup tasks T036–T038 run in parallel.
- Polish verification tasks T039–T040 run in parallel.

---

## Parallel Example: User Story 1

```bash
# Parallel test preparation
T008 + T009 + T010 + T011

# Parallel frontend alignment once backend contract is stable
T020 + T021 + T022
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 + Phase 2.
2. Complete Phase 3 (US1) end-to-end.
3. Validate independent test criteria for US1 before expanding scope.

### Incremental Delivery

1. Ship US1 (semantics + strict validation + atomic CSV import).
2. Add US2 (closed-loop valuation + CF correctness).
3. Add US3 (final UX/help wording and terminology consistency).
4. Run Phase 6 cross-cutting checks.

### Parallel Team Strategy

1. One backend stream handles US1 backend contract/validation + CSV import.
2. Second backend stream handles US2 valuation/CF updates after foundational phase.
3. Frontend stream handles US1/US3 label/help updates once enum naming is finalized.
