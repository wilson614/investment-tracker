# Research: Closed-Loop Performance Model & Transaction Type Redesign

**Feature**: 011-closed-loop-performance-model
**Date**: 2026-02-13

## Decision 1: Replace legacy enum semantics with explicit closed-loop categories

**Decision**
- Rename and redefine currency transaction categories to encode business intent directly:
  - `InitialBalance` → `TransferInBalance` (explicit external inflow)
  - Add dedicated stock-linked categories for buy/sell-linked ledger events (replacing generic mapping)
  - Keep `OtherIncome` as single explicit external inflow category
  - Keep `OtherExpense` as explicit external outflow-only category
  - Add dedicated dividend category distinct from interest/other income

**Rationale**
- Current enum names overload meaning and mix internal/external semantics, causing ambiguity in validation, CSV, and return CF policy.
- The spec explicitly requires no compatibility aliases and no legacy naming retention (FR-001/002/003/004/005/008/015).

**Alternatives considered**
1. Keep legacy names and document behavior only — rejected (does not satisfy FR-008 and keeps ambiguity).
2. Keep aliases temporarily — rejected (user explicitly accepted breaking enum change).

---

## Decision 2: Enforce backend-first ledger-currency/type validation for create/update

**Decision**
- Define a centralized validation policy matrix (TWD vs non-TWD ledger) and enforce it in backend create/update flow.
- Keep request DTOs strict; no fallback alias parsing.

**Rationale**
- Current backend validation is mostly field-shape validation and does not fully enforce ledger-currency/type policy.
- Frontend-only restrictions are insufficient and violate FR-006.

**Alternatives considered**
1. Validate only in frontend forms/import — rejected (bypassable, violates FR-006).
2. Soft warning then accept — rejected (spec requires strict rejection for invalid combinations).

---

## Decision 3: Introduce backend currency CSV import with all-or-nothing transaction boundary

**Decision**
- Implement server-side currency CSV import endpoint/use case that:
  1) parses all rows,
  2) validates all rows against the same backend policy,
  3) returns complete diagnostics in one response when any row invalid,
  4) commits only when entire batch valid.

**Rationale**
- Current import path is frontend row-by-row create calls, resulting in partial success behavior.
- FR-007/007a/007b require strict all-or-nothing and full error set with row/field/value/correction guidance.

**Alternatives considered**
1. Keep row-by-row API calls and aggregate UI errors — rejected (cannot guarantee atomicity).
2. Fail-fast first-error response — rejected (violates FR-007b complete error set requirement).

---

## Decision 4: Unify valuation baseline for MD and TWR to strict closed-loop `stock + ledger`

**Decision**
- Align annual performance pipeline so both Modified Dietz and TWR consume the same valuation baseline at period boundaries and snapshots: stock market value plus bound ledger balance.

**Rationale**
- Current code path mixes baseline sources (stock-only in parts of annual summary vs snapshot-based in TWR flow), creating inconsistency.
- FR-009/FR-012 explicitly require the same closed-loop baseline for both metrics.

**Alternatives considered**
1. Keep current mixed baseline for backward continuity — rejected (fails FR-012 consistency).
2. Compute MD from TWR subperiod outputs only — rejected (not aligned with current architecture intent and adds unnecessary complexity).

---

## Decision 5: Remove ledger non-positive floor in valuation logic

**Decision**
- Remove any floor-to-zero behavior for non-positive ledger balances in valuation services used by performance calculations.

**Rationale**
- Current snapshot valuation includes explicit `balance <= 0 => 0` logic, which breaks closed-loop net asset reality.
- FR-010 mandates negative ledger reduces total net assets and must not be clamped.

**Alternatives considered**
1. Keep floor and expose warning — rejected (still numerically wrong under spec).
2. Floor only for UI display — rejected for calculation path (can be considered separately for presentation, not metric computation).

---

## Decision 6: Rebuild return cash-flow inclusion policy around explicit external events only

**Decision**
- Define explicit CF include set for both MD/TWR and exclude internal reallocations:
  - Include: `TransferInBalance`, `Deposit`, `Withdraw`, `OtherIncome`, `OtherExpense`, non-TWD `ExchangeBuy`, non-TWD `ExchangeSell`
  - Exclude: stock buy/sell-linked internal events, internal FX effects, interest/dividend internal returns

**Rationale**
- Existing `ReturnCashFlowStrategy` currently includes only a subset (`InitialBalance/Deposit/Withdraw`) and may treat stock events as CF under stock strategy.
- FR-011/011a/011b/011c require explicit external-only policy and non-TWD exchange flows inclusion.

**Alternatives considered**
1. Keep current strategy and patch edge cases — rejected (policy remains fragmented).
2. Include all ledger movements — rejected (would pollute CF with internal reallocations).

---

## Decision 7: Synchronize frontend labels/help text/import-export vocabulary with redesigned enum

**Decision**
- Update all user-facing transaction type labels (forms, detail tables, CSV import parser/export labels) to match new enum semantics and naming.
- Update metric help text strings to:
  - MD: `衡量比例的重壓 (Modified Dietz)`
  - TWR: `衡量本金的重壓 (TWR)`

**Rationale**
- Current frontend has inconsistent wording for several categories and old MD/TWR helper copy.
- FR-013/014/015 require deterministic wording and naming alignment.

**Alternatives considered**
1. Backend-only rename and keep old UI labels — rejected (violates FR-015, increases user confusion).
2. Partial update only in primary form — rejected (labels also exist in detail page/import/export/help text).

---

## Decision 8: Testing focus must target correctness regressions, not broad rewrite

**Decision**
- Add/adjust tests around:
  - enum + ledger-currency/type validation matrix,
  - all-or-nothing CSV import full diagnostics,
  - closed-loop valuation with negative ledger,
  - explicit CF set behavior for MD/TWR,
  - frontend help text + label mapping updates.

**Rationale**
- Existing suite has strong formula-level tests but notable gaps in currency import and controller/update validation coverage.
- This feature is correctness-critical and semantics-heavy; targeted regressions are required to protect future changes.

**Alternatives considered**
1. Rely on existing integration tests only — rejected (coverage gaps remain in CSV and enum semantic transitions).
2. Full E2E-first approach — rejected for planning scope; can be layered after core regression set.
