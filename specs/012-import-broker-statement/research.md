# Phase 0 Research: Unified Broker Statement Import

## Decision 1: Single import entry supports two CSV schemas via detection + override

- **Decision**: Keep one stock import entry and add schema detection after upload, while allowing user override before preview/execution.
- **Rationale**:
  - Existing flow already has a reusable preview modal and column mapping engine (`CSVImportModal`, `csvParser`) and can present pre-execution review.
  - This satisfies FR-001/FR-002/FR-003 without introducing a second import UI path.
- **Alternatives considered**:
  - Separate import entry for broker statements: rejected due to duplicated UX and higher maintenance.
  - Manual format-only selection (no detection): rejected because it increases user friction and error rate.

## Decision 2: Move stock import from row-by-row POST to batch import contract

- **Decision**: Introduce stock batch import API contract with explicit preview/execute semantics and row-level diagnostics summary.
- **Rationale**:
  - Current stock import submits per row via `transactionApi.create` and can partial-success unpredictably.
  - Existing backend examples (`CurrencyTransactionsController` all-or-nothing diagnostics; `BankAccountsController` preview/execute) provide proven patterns.
  - Batch contract is required to enforce unresolved-balance/unresolved-symbol gating deterministically.
- **Alternatives considered**:
  - Continue row-by-row POST and handle errors purely in frontend: rejected because server-side consistency and deterministic result aggregation are weaker.
  - Full all-or-nothing for stock import: rejected because FR-006c requires resolvable rows to proceed even when sync fails for unresolved rows.

## Decision 3: Reuse manual-create balance handling semantics exactly

- **Decision**: For buy rows with shortfall, require explicit `BalanceAction` (Margin/TopUp) and `TopUpTransactionType` rules identical to manual create logic in `CreateStockTransactionUseCase`.
- **Rationale**:
  - Existing business rules are already authoritative and tested around `BalanceAction.None/Margin/TopUp`.
  - Aligns with user requirement to match manual add behavior.
- **Alternatives considered**:
  - New import-specific balance policy: rejected due to behavior divergence and financial inconsistency risk.

## Decision 4: Security identity resolution uses local mapping + on-demand sync

- **Decision**: Add TW security mapping persistence (local DB as primary), attempt on-demand TWSE synchronization only when local mapping misses, and require manual ticker input for unresolved rows.
- **Rationale**:
  - User clarified: no periodic sync; sync on miss only.
  - Existing Euronext mapping service demonstrates a robust "lookup local -> fetch remote -> upsert local" pattern.
  - Keeps core feature usable in self-hosted environments with intermittent external availability.
- **Alternatives considered**:
  - Always-online lookup during import: rejected due to availability and latency risks.
  - Periodic background synchronization: rejected by user decision in clarify session.

## Decision 5: Synchronization failure handling is partial-continue with guided remediation

- **Decision**: If TWSE sync fails, continue processing already resolvable rows; list unresolved rows in original order with parsed security name and require manual ticker input before execution of those rows.
- **Rationale**:
  - Directly matches FR-006c/FR-006d and user clarification.
  - Balances throughput and correctness (no forced stop, no silent wrong mapping).
- **Alternatives considered**:
  - Hard fail entire import on sync failure: rejected (too disruptive).
  - Auto-skip unresolved rows without explicit remediation: rejected (hidden data loss risk).

## Decision 6: Reuse existing diagnostic shape for row-level error reporting

- **Decision**: Model stock import diagnostics after `CurrencyTransactionCsvImportErrorDto` style (row number, field, invalid value, code, message, correction guidance).
- **Rationale**:
  - Existing frontend rendering and API patterns already support this structure.
  - Improves consistency across import features and simplifies UI integration.
- **Alternatives considered**:
  - Free-form message-only errors: rejected due to weak machine-readability and poor UX remediation.

## Decision 7: Broker statement side inference rule is canonical sign-based with confirmation fallback

- **Decision**: Derive side from net settlement sign (`negative=buy`, `positive=sell`); if ambiguity remains, require per-row confirmation before execution.
- **Rationale**:
  - Explicitly decided in clarify session and codified as FR-005/FR-005a.
  - Preserves automation while preventing silent misclassification.
- **Alternatives considered**:
  - Require explicit side column only: rejected due to common broker statement limitations.
  - Fully heuristic text parsing only: rejected due to instability across brokers/wording.

## Decision 8: Keep technical context stack unchanged

- **Decision**: Implement within existing .NET 8 + React + PostgreSQL architecture and test stacks.
- **Rationale**:
  - Constitution and project constraints require stack consistency.
  - Existing import and market-data modules already provide reusable infrastructure.
- **Alternatives considered**:
  - Introduce external ETL service or queue: rejected as unnecessary complexity for current scope.
