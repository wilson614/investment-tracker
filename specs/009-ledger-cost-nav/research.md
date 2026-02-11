# Research: Ledger Exchange Cost Integration & Navigation Improvement

**Feature**: 009-ledger-cost-nav
**Date**: 2026-02-11

## Design Decisions

### 1. LIFO Rate Preview API Endpoint

**Decision**: Add a new endpoint `GET /api/currency-ledgers/{id}/exchange-rate-preview` to calculate and return the LIFO exchange rate for a given purchase amount and date.

**Rationale**: The frontend needs to display the calculated exchange rate as read-only information before the user submits the stock transaction. This requires the LIFO calculation to be exposed as a preview endpoint.

**Alternatives considered**:
- Calculate rate on frontend → Rejected: LIFO logic is complex domain logic that belongs in the backend.
- Only calculate on submit and return in response → Rejected: User needs to see the rate before submitting to make informed decisions (e.g., whether to proceed or cancel).
- Embed in existing stock transaction create endpoint as a "dry run" → Rejected: Overcomplicates the existing endpoint.

**Parameters**: `amount` (decimal), `date` (DateTime)
**Returns**: `{ rate: decimal, source: "lifo" | "market" | "blended", lifoRate: decimal?, marketRate: decimal?, lifoPortion: decimal?, marketPortion: decimal? }`

### 2. Weighted Blend Calculation for Partial Margin

**Decision**: Add a new method `CalculateExchangeRateWithMargin` in `CurrencyLedgerService` that computes a weighted blend when the purchase amount exceeds ledger balance.

**Rationale**: When partial margin is used, the stock transaction's exchange rate must reflect two sources: the LIFO cost for the covered portion and the market rate for the margin portion.

**Formula**: `blendedRate = (coveredAmount × lifoRate + marginAmount × marketRate) / totalAmount`

**Alternatives considered**:
- Use only market rate for entire purchase when margin → Rejected: User explicitly wants LIFO for the covered portion.
- Use only LIFO rate (capped at balance) → Rejected: The margin portion has no LIFO cost; market rate is the correct reference.

### 3. Replace AutoDeposit Boolean with BalanceAction Enum

**Decision**: Replace `AutoDeposit: bool` in `CreateStockTransactionRequest` with `BalanceAction: enum (None, Margin, TopUp)` and add `TopUpTransactionType: CurrencyTransactionType?`.

**Rationale**: The binary AutoDeposit flag cannot express the three-option model (Margin / Top Up / Cancel). An enum is clearer and extensible.

**Mapping from old to new**:
| Old | New |
|-----|-----|
| `AutoDeposit: true` | `BalanceAction: TopUp, TopUpTransactionType: ExchangeBuy` |
| `AutoDeposit: false` (continue) | `BalanceAction: Margin` |
| (form cancel) | `BalanceAction: None` (or simply don't submit) |

**Alternatives considered**:
- Two separate API calls (create currency tx, then stock tx) → Rejected: Loses atomicity; if second call fails, orphaned currency transaction exists.
- Keep bool + add separate field → Rejected: Less expressive, confusing API surface.

### 4. Exchange Rate Field UX Change

**Decision**: Replace the editable exchange rate `<input>` with a read-only display element. The rate is fetched via the new preview API when the user fills in amount/date.

**Rationale**: User confirmed they want the rate to be system-calculated only, with no manual override on the stock transaction form. Transparency is maintained by showing the calculated rate.

**Implementation approach**:
- When `ticker`, `shares`, `pricePerShare`, or `transactionDate` changes, call the preview API.
- Display the rate with a label indicating the source ("帳本成本" for LIFO, "市場匯率" for market fallback).
- Show loading state while fetching.
- Show error state if rate unavailable, with guidance to create ledger records.

**Alternatives considered**:
- Keep editable field but pre-fill with LIFO rate → Rejected by user: they want to prevent manual entry entirely.
- Hide the rate completely → Rejected: user needs to see what rate is being used for transparency.

### 5. Insufficient Balance Modal Redesign

**Decision**: Replace the existing 2-button `ConfirmationModal` with a 3-option modal including a sub-form for the Top Up option.

**Rationale**: The three options (Margin, Top Up with type selection, Cancel) require more UI than the current simple confirmation dialog.

**Modal flow**:
1. Modal shows shortfall amount and three option buttons.
2. "融資 (Margin)" → Submits immediately with `BalanceAction.Margin`.
3. "補足餘額 (Top Up)" → Expands to show transaction type dropdown + exchange rate info → On confirm, submits with `BalanceAction.TopUp` and selected type.
4. "取消 (Cancel)" → Closes modal, returns to form.

**Transaction types available for Top Up**: ExchangeBuy, Deposit, InitialBalance, OtherIncome, Interest (all "income" types that increase balance).

### 6. Ledger Navigation Fix

**Decision**: Read `selected_ledger_id` from localStorage directly in `Navigation.tsx` to construct the ledger link URL.

**Rationale**: The `LedgerContext` already stores the last-selected ledger ID in localStorage. The navigation component just needs to read this value to construct the correct URL.

**Implementation**:
- In `Navigation.tsx`, read `localStorage.getItem('selected_ledger_id')` on render.
- If value exists: link to `/ledger/${selectedLedgerId}`.
- If value does not exist: link to `/ledger` (existing redirect behavior).
- Both desktop and mobile nav links need updating.

**Alternatives considered**:
- Use `useLedger()` context hook → Works but adds unnecessary re-renders when ledger context changes. localStorage read is simpler and sufficient.
- Create a custom hook `useLastSelectedLedgerId()` → Over-engineering for a single localStorage read.

## Dependencies

| Dependency | Type | Risk |
|-----------|------|------|
| `CurrencyLedgerService.CalculateExchangeRateForPurchase` | Existing domain service | Low — already implemented and tested |
| `ITransactionDateExchangeRateService.GetOrFetchAsync` | Existing service | Low — already used in CreateStockTransactionUseCase |
| `CurrencyLedgerService.CalculateBalance` | Existing domain service | Low — already used in CreateStockTransactionUseCase |
| External exchange rate API (Stooq) | External service | Medium — may fail; fallback handling already exists |
