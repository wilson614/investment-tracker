# Feature Specification: Closed-Loop Performance Model & Transaction Type Redesign

**Feature Branch**: `011-closed-loop-performance-model`
**Created**: 2026-02-12
**Status**: Draft
**Input**: User description: "Closed-loop performance model and currency transaction type redesign with strict backend/CSV validation, enum renaming, explicit CF policy, total net asset valuation including negative ledger, and updated MD/TWR help wording."

## Clarifications

### Session 2026-02-12

- Q: Should request compatibility aliases for legacy enum names be kept? → A: No — backward compatibility aliases are not required because data can be re-imported.
- Q: Should `OtherIncome` remain and count as cash flow? → A: Yes — keep `OtherIncome` and include it in CF.
- Q: Should `InitialBalance` semantics and naming be aligned to "Transfer In Balance" / "轉入餘額"? → A: Yes.
- Q: How should transaction-type semantics handle stock buy/sell linkage and dividend? → A: Split sell-linked event from generic `OtherIncome`, add a dedicated dividend type that is not CF, and rename buy-linked outflow to a stock-specific label.
- Q: Should closed-loop valuation include negative ledger balances? → A: Yes — negative ledger balance must reduce total net assets and must not be floored to zero.
- Q: When a CSV import contains any invalid row, should valid rows still be imported? → A: No — CSV import is all-or-nothing; if any row is invalid, the entire batch is rejected.
- Q: In all-or-nothing CSV rejection, what feedback must be shown to users? → A: The system must clearly report where data is wrong and what to change (row, field, invalid value, and correction guidance).
- Q: Should `TransferInBalance` (renamed from `InitialBalance`) be included in return cash-flow events? → A: Yes — it is an explicit external inflow with cost basis and must be included in CF.
- Q: For non-TWD ledgers, should `ExchangeBuy` be treated as external cash-flow inflow? → A: Yes — treat `ExchangeBuy` as explicit external inflow for non-TWD ledgers and include it in CF.
- Q: Should `OtherExpense` be restricted to external outflow semantics only? → A: Yes — `OtherExpense` is external outflow only and always included in CF (stock fees remain in stock transactions).
- Q: What exact replacement wording should be used for metric help text? → A: Replace MD phrasing with "衡量比例的重壓 (Modified Dietz)" and apply the same pattern to TWR phrasing.
- Q: For non-TWD ledgers, should `ExchangeSell` be treated as external cash-flow outflow? → A: Yes — treat `ExchangeSell` as explicit external outflow for non-TWD ledgers and include it in CF as a negative flow.
- Q: Should `OtherIncome` require a mandatory subcategory field? → A: No — keep a single `OtherIncome` category without mandatory subcategory splitting.
- Q: For rejected CSV files, should validation return only first error or full error set? → A: Return the full error set in one response (all invalid rows/fields), not only the first error.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unified Transaction Semantics and Validation (Priority: P1)

As a user managing ledger transactions, I need transaction types to have clear and consistent business meaning across backend APIs, manual entry, and CSV import, so that invalid type/currency combinations are blocked and return metrics are based on correct event semantics.

**Why this priority**: If transaction semantics are ambiguous or inconsistent between entry paths, all downstream valuation and return metrics become unreliable.

**Independent Test**: Submit transaction create/update requests and CSV rows for both TWD and non-TWD ledgers; verify invalid combinations are rejected consistently and valid combinations are accepted consistently regardless of entry channel.

**Acceptance Scenarios**:

1. **Given** a TWD ledger, **When** a transaction uses a type not allowed for that ledger currency policy, **Then** backend validation rejects the request with a clear business error.
2. **Given** a non-TWD ledger, **When** a CSV file contains any row with a type that violates ledger-currency rules, **Then** the entire import batch is rejected and row-level validation messages are shown, including row number, field name, invalid value, and suggested correction.
3. **Given** a stock buy/sell linked ledger event, **When** linked transactions are generated, **Then** buy-linked and sell-linked events use dedicated stock-linked categories rather than generic other-income/other-expense categories.
4. **Given** a dividend entry, **When** it is recorded and displayed, **Then** it uses the dedicated dividend category and is distinguishable from broker rebate and stock-sell proceeds.

---

### User Story 2 - Closed-Loop Valuation and Return Calculation (Priority: P1)

As a user reviewing annual performance, I need total net assets to be calculated as stock market value plus bound ledger balance (including negative balances), and I need TWR/Modified Dietz to use explicit external cash-flow events only, so that returns reflect a closed-loop account model.

**Why this priority**: Return metrics are the core product outcome; if valuation base or CF policy is wrong, decisions made from analytics become misleading.

**Independent Test**: Run annual performance on controlled fixtures (positive and negative ledger balance, mixed internal/explicit external events) and verify value base and return outputs follow the closed-loop CF policy.

**Acceptance Scenarios**:

1. **Given** a ledger balance below zero, **When** total net assets are evaluated at any date, **Then** the negative balance reduces total value instead of being treated as zero.
2. **Given** internal reallocations only (stock buy/sell linked events, internal FX transfers, interest/dividend internal return), **When** annual return is calculated, **Then** these events do not create external CF events, and dividend does not contribute to external CF inputs for either Modified Dietz or TWR.
3. **Given** explicit external in/out events (including `ExchangeBuy` and `ExchangeSell` on non-TWD ledgers), **When** annual return is calculated, **Then** only these events contribute to CF inputs for both Modified Dietz and TWR.
4. **Given** the same period valuation path, **When** return metrics are produced, **Then** both Modified Dietz and TWR are computed on the same total-net-assets baseline (stock + ledger cash).

---

### User Story 3 - UX Copy and User Interpretation Alignment (Priority: P2)

As a user reading metric help text, I need clear and consistent wording for MD and TWR interpretation, so that I understand each metric using the updated terminology and avoid prior interpretation confusion.

**Why this priority**: Terminology directly affects user interpretation and trust in performance analytics.

**Independent Test**: Open the metric help UI and confirm wording updates are shown in all relevant views and are consistent with the new model language.

**Acceptance Scenarios**:

1. **Given** the MD help text entry, **When** the user opens the info tooltip/description, **Then** wording uses "衡量比例的重壓 (Modified Dietz)".
2. **Given** the TWR help text entry, **When** the user opens the info tooltip/description, **Then** wording uses "衡量本金的重壓 (TWR)".
3. **Given** both metrics are shown together, **When** the user compares definitions, **Then** wording clearly distinguishes the two concepts without reusing the deprecated phrasing.

---

### Edge Cases

- What happens when historical data contains legacy transaction types that no longer match the new semantics? Data migration is not required; invalid legacy semantics may be corrected via re-import workflow.
- What happens when a single CSV upload contains both valid and invalid rows? The entire file is rejected (all-or-nothing), and row-level errors are returned for each invalid row with row number, field name, invalid value, and correction guidance.
- What happens when `OtherIncome` is used for both broker rebate and non-rebate inputs? The system must preserve explicit categorization rules so sell-linked proceeds are never mapped to generic `OtherIncome`.
- What happens when `OtherExpense` is used for internal purposes? It is invalid; `OtherExpense` is reserved for external outflow semantics only.
- What happens when no explicit external in/out event exists in a period but internal activities are present? Return calculation must proceed with zero CF events from internal categories.
- What happens when ledger balance is deeply negative near period boundaries? Total net assets may be negative and calculations must still use the closed-loop valuation policy without clamping.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST redefine transaction category semantics so stock buy-linked outflow uses a dedicated stock-linked category and stock sell-linked inflow uses a dedicated stock-linked category, separate from generic other-income/other-expense categories.
- **FR-002**: System MUST rename the current initial-balance category to transfer-in-balance semantics and keep its behavior as explicit external inflow with cost basis.
- **FR-003**: System MUST introduce a dedicated dividend-income category that is distinct from interest and other-income categories.
- **FR-003a**: Dividend MUST be treated as an internal return component and MUST NOT be included in explicit external cash-flow inputs for Modified Dietz or TWR.
- **FR-004**: System MUST treat `OtherIncome` as an explicit external cash-flow inflow category.
- **FR-004a**: `OtherIncome` MUST remain a single category and MUST NOT require mandatory subcategory splitting.
- **FR-005**: System MUST treat `OtherExpense` as an explicit external cash-flow outflow category only, and this category MUST NOT be used to represent stock trading fees.
- **FR-006**: Backend MUST enforce strict ledger-currency/type validation rules for transaction create/update operations regardless of frontend behavior.
- **FR-007**: CSV import MUST apply the same ledger-currency/type validation rules as backend transaction entry and MUST enforce all-or-nothing behavior (if any row is invalid, no rows are imported) with actionable row-level messages.
- **FR-007a**: For all rejected CSV files, the system MUST provide per-error diagnostics containing row number, field name, invalid value, and clear correction guidance so users can fix and re-import.
- **FR-007b**: CSV validation feedback for a rejected file MUST include the complete error set (all invalid rows/fields) in a single response, not first-error-only behavior.
- **FR-008**: System MUST NOT provide request compatibility aliases for deprecated enum names in the new model; clients are expected to use the new enum names and semantics.
- **FR-009**: Total net asset valuation MUST use the formula `V_t = stock market value + bound ledger balance`.
- **FR-010**: Ledger balance MUST be allowed to be negative and MUST reduce total net assets; valuation logic MUST NOT floor negative balances to zero.
- **FR-011**: Return cash-flow policy for Modified Dietz and TWR MUST include only explicit external in/out categories and MUST exclude internal reallocations (including stock buy/sell linked events, internal FX transfer effects, and internal return categories such as interest/dividend).
- **FR-011a**: `TransferInBalance` (renamed from `InitialBalance`) MUST be treated as an explicit external inflow category and MUST be included in CF inputs for both Modified Dietz and TWR.
- **FR-011b**: For non-TWD ledgers, `ExchangeBuy` MUST be treated as an explicit external inflow category and MUST be included in CF inputs for both Modified Dietz and TWR.
- **FR-011c**: For non-TWD ledgers, `ExchangeSell` MUST be treated as an explicit external outflow category and MUST be included in CF inputs for both Modified Dietz and TWR as a negative flow.
- **FR-012**: Modified Dietz and TWR MUST both be computed on the same closed-loop total-net-assets baseline (stock + ledger cash, including negative ledger).
- **FR-013**: UI copy for MD metric explanation MUST replace prior wording with "衡量比例的重壓 (Modified Dietz)".
- **FR-014**: UI copy for TWR metric explanation MUST replace prior wording with "衡量本金的重壓 (TWR)".
- **FR-015**: Transaction type labels shown in forms, detail pages, and export/import mappings MUST align with the redesigned enum semantics and naming.

### Key Entities *(include if feature involves data)*

- **Currency Transaction Category**: Classification for ledger events with explicit business semantics (external in/out, stock-linked internal movement, internal return), used by validation, import, display labels, and return-CF policy.
- **Closed-Loop Total Net Asset Snapshot**: Time-point valuation combining stock market value and bound ledger balance, where ledger can be positive or negative.
- **Return Cash-Flow Event**: Derived event set used by Modified Dietz and TWR that includes only explicit external in/out categories.
- **Metric Help Definition**: User-facing explanatory text for MD and TWR interpretation terms used in tooltip/help UI.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of transaction create/update attempts that violate ledger-currency/type rules are rejected consistently by backend validation in acceptance tests.
- **SC-002**: In CSV import validation tests, if any row violates type/currency rules the import result is 0 rows written, and per-error diagnostics (row number, field name, invalid value, correction guidance) are returned for 100% of violating rows in that file.
- **SC-003**: In controlled regression fixtures that include negative ledger balances, computed total net assets match expected `stock + ledger` values at all asserted checkpoints with no zero-flooring behavior.
- **SC-004**: In return regression fixtures, only explicit external in/out categories are present in CF inputs, and internal categories contribute zero CF events.
- **SC-004a**: In fixtures that include dividend and at least one explicit external event, dividend contributes 0 to external CF inputs for both Modified Dietz and TWR, while explicit external events remain included.
- **SC-005**: In UI verification, MD and TWR help text wording appears as "衡量比例的重壓 (Modified Dietz)" and "衡量本金的重壓 (TWR)" in all target screens, with 0 occurrences of deprecated wording.

## Assumptions

- Users accept breaking changes in enum naming and semantics and can re-import data to conform to the new model.
- Each portfolio remains bound to exactly one ledger as the valuation cash component source.
- The product continues to treat stock trading fees as part of stock transaction economics, not as standalone external ledger cash-flow events.
- Explicit external in/out categories are the authoritative source for return cash-flow events in the closed-loop model.

## Out of Scope

- Automatic migration of historical records to the new transaction-category semantics.
- Backward compatibility adapters for deprecated enum string names in request payloads.
- Redesign of unrelated dashboard/selector workflows outside this closed-loop performance and transaction-type scope.