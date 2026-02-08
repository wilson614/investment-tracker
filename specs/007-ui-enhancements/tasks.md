# Tasks: UI Enhancements Batch

**Input**: Design documents from `/specs/007-ui-enhancements/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: No test tasks included (not explicitly requested in specification).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Web app**: `backend/src/`, `frontend/src/`
- Backend: `backend/src/InvestmentTracker.Api/`, `backend/src/InvestmentTracker.Domain/`
- Frontend: `frontend/src/components/`, `frontend/src/pages/`, `frontend/src/contexts/`

---

## Phase 1: Setup

**Purpose**: No new project setup needed - all changes are to existing codebase

- [ ] T001 Verify branch is on `007-ui-enhancements` and sync with master

**Checkpoint**: Ready to implement user stories

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: No foundational tasks required - all user stories can use existing infrastructure

**⚠️ NOTE**: This feature batch modifies existing code only. No new shared infrastructure needed.

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - Fix Stock Trading Dialog Flicker (Priority: P1) 🎯 MVP

**Goal**: Remove Linked Ledger section and compact notes field to eliminate flicker and fit dialog in viewport

**Independent Test**: Open stock trading dialog → verify no flicker, no Linked Ledger section, dialog fits 1080p screen

### Implementation for User Story 1

- [ ] T002 [US1] Remove Linked Ledger section from `frontend/src/components/transactions/TransactionForm.tsx` (lines ~461-500)
- [ ] T003 [US1] Change notes field from textarea to single-line input in `frontend/src/components/transactions/TransactionForm.tsx`
- [ ] T004 [US1] Remove unused imports and state related to boundLedger in `frontend/src/components/transactions/TransactionForm.tsx`
- [ ] T005 [US1] Verify dialog layout fits viewport on 1080p without scrolling

**Checkpoint**: Stock trading dialog renders without flicker and fits viewport

---

## Phase 4: User Story 2 - Ledger Dropdown Navigation (Priority: P2)

**Goal**: Replace ledger overview page with dropdown selector similar to portfolio navigation

**Independent Test**: Navigate to ledger section → verify dropdown shows in top-left, switching ledgers works without page reload, selection persists

### Implementation for User Story 2

- [ ] T006 [P] [US2] Create `frontend/src/contexts/LedgerContext.tsx` following PortfolioContext pattern
- [ ] T007 [P] [US2] Create `frontend/src/components/ledger/LedgerSelector.tsx` following PortfolioSelector pattern
- [ ] T008 [US2] Add LedgerProvider to app wrapper in `frontend/src/App.tsx`
- [ ] T009 [US2] Refactor `frontend/src/pages/Currency.tsx` to auto-redirect to last selected ledger using LedgerContext
- [ ] T010 [US2] Integrate LedgerSelector into header of `frontend/src/pages/CurrencyDetail.tsx`
- [ ] T011 [US2] Update navigation/routing to remove ledger overview page link (direct to CurrencyDetail)
- [ ] T012 [US2] Add localStorage persistence for selected ledger (key: `selected_ledger_id`)

**Checkpoint**: Ledger navigation works via dropdown, selection persists across sessions

---

## Phase 5: User Story 3 - Bank Account Export/Import (Priority: P3)

**Goal**: Add CSV export and import functionality for bank accounts following existing patterns

**Independent Test**: Export bank accounts → modify CSV → import → verify data integrity preserved

### Backend Implementation for User Story 3

- [ ] T013 [P] [US3] Create DTOs in `backend/src/InvestmentTracker.Api/Dtos/BankAccountImportDto.cs`
- [ ] T014 [US3] Add Export endpoint (GET /api/bank-accounts/export) to `backend/src/InvestmentTracker.Api/Controllers/BankAccountsController.cs`
- [ ] T015 [US3] Add Import endpoint (POST /api/bank-accounts/import) with preview/execute modes to `backend/src/InvestmentTracker.Api/Controllers/BankAccountsController.cs`
- [ ] T016 [US3] Implement duplicate detection logic (by BankName) in import endpoint

### Frontend Implementation for User Story 3

- [ ] T017 [P] [US3] Add `exportBankAccountsToCSV` function to `frontend/src/services/csvExport.ts`
- [ ] T018 [P] [US3] Create `frontend/src/components/import/BankAccountImportButton.tsx` following StockImportButton pattern
- [ ] T019 [US3] Create `frontend/src/components/import/BankAccountImportModal.tsx` with preview functionality
- [ ] T020 [US3] Add Export button to `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`
- [ ] T021 [US3] Add Import button to `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`
- [ ] T022 [US3] Implement CSV parsing and validation in import modal
- [ ] T023 [US3] Implement import preview display showing create/update actions
- [ ] T024 [US3] Implement import confirmation and execution flow

**Checkpoint**: Bank account export/import works with full round-trip data integrity

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and cleanup

- [ ] T025 Run manual verification per quickstart.md scenarios
- [ ] T026 Verify all acceptance scenarios from spec.md pass
- [ ] T027 Code cleanup - remove any dead code from removed Linked Ledger section

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify branch state
- **Foundational (Phase 2)**: N/A for this feature batch
- **User Stories (Phase 3-5)**: All independent - can proceed in any order
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: No dependencies - frontend only, modifies existing component
- **User Story 2 (P2)**: No dependencies - frontend only, creates new context/component
- **User Story 3 (P3)**: No dependencies - full-stack, follows existing patterns

**Note**: All three user stories are completely independent and can be implemented in parallel.

### Within Each User Story

- Backend tasks before frontend integration (US3 only)
- Context/state before components that consume it (US2)
- Core changes before cleanup tasks

### Parallel Opportunities

- T006 and T007 can run in parallel (different new files)
- T013, T017, T018 can run in parallel (different files, no dependencies)
- All three user stories can be assigned to different developers simultaneously

---

## Parallel Example: User Story 2

```bash
# Launch context and component creation together:
Task: "Create LedgerContext.tsx" (T006)
Task: "Create LedgerSelector.tsx" (T007)

# Then sequentially:
Task: "Add LedgerProvider to App.tsx" (T008)
Task: "Refactor Currency.tsx" (T009)
Task: "Integrate LedgerSelector into CurrencyDetail.tsx" (T010)
```

## Parallel Example: User Story 3

```bash
# Launch backend DTO and frontend export function together:
Task: "Create DTOs" (T013)
Task: "Add exportBankAccountsToCSV" (T017)
Task: "Create BankAccountImportButton" (T018)

# Then sequentially:
Task: "Add Export endpoint" (T014)
Task: "Add Import endpoint" (T015)
Task: "Create BankAccountImportModal" (T019)
# ... etc
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete T001 (Setup verification)
2. Complete T002-T005 (User Story 1)
3. **STOP and VALIDATE**: Test dialog manually
4. Deploy/demo if ready

### Incremental Delivery

1. User Story 1 → Bug fix deployed (immediate user value)
2. User Story 2 → UX improvement deployed
3. User Story 3 → New feature deployed
4. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:
1. Developer A: User Story 1 (P1) - Quick win
2. Developer B: User Story 2 (P2) - Frontend focus
3. Developer C: User Story 3 (P3) - Full-stack

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Reference files: PortfolioContext.tsx, PortfolioSelector.tsx, csvExport.ts, StockImportButton.tsx
