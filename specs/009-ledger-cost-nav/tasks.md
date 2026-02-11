# Tasks: Ledger Exchange Cost Integration & Navigation Improvement

**Input**: Design documents from `/specs/009-ledger-cost-nav/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Not explicitly requested in spec — test tasks not included.

**Organization**: Tasks grouped by user story. US1 and US2 share backend files (CreateStockTransactionUseCase, TransactionForm.tsx) but are split into sequential phases for independent testability.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create new additive files that don't break existing code

- [ ] T001 Create `BalanceAction` enum (None=0, Margin=1, TopUp=2) with XML doc comments in `backend/src/InvestmentTracker.Domain/Enums/BalanceAction.cs`
- [ ] T002 [P] Add `ExchangeRatePreviewResponse` record DTO (Rate, Source, LifoRate, MarketRate, LifoPortion, MarketPortion) in `backend/src/InvestmentTracker.Application/DTOs/` (new file or append to existing ResponseDtos)
- [ ] T003 [P] Add `CalculateExchangeRateWithMargin` method to `backend/src/InvestmentTracker.Domain/Services/CurrencyLedgerService.cs` — accepts ledger transactions, purchase date, purchase amount, current balance, and market rate; returns weighted blend of LIFO rate (for covered portion) and market rate (for margin portion) using formula: `(coveredAmount × lifoRate + marginAmount × marketRate) / totalAmount`. Delegates to existing `CalculateExchangeRateForPurchase` for the LIFO portion.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: DTO and frontend type changes that MUST be complete before user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 Modify `CreateStockTransactionRequest` in `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs` — remove `ExchangeRate` (decimal?) and `AutoDeposit` (bool) fields; add `BalanceAction` (BalanceAction enum, default None) and `TopUpTransactionType` (CurrencyTransactionType?, nullable). Update any validation attributes.
- [ ] T005 [P] Add `BalanceAction` TypeScript enum type (None=0, Margin=1, TopUp=2) and `ExchangeRatePreviewResponse` interface to `frontend/src/types/index.ts`
- [ ] T006 [P] Add `getExchangeRatePreview(ledgerId: string, amount: number, date: string)` API function in `frontend/src/api/` (e.g., in currencyApi or transactionApi) — calls `GET /api/currency-ledgers/{id}/exchange-rate-preview?amount={amount}&date={date}`, returns `ExchangeRatePreviewResponse`

**Checkpoint**: Foundation ready — all new types and APIs available for user story implementation

---

## Phase 3: User Story 1 — Stock Purchase Uses Ledger Exchange Cost (Priority: P1) 🎯 MVP

**Goal**: Foreign-currency stock Buy transactions automatically use the LIFO weighted-average exchange rate from the currency ledger. The exchange rate is displayed as read-only on the transaction form.

**Independent Test**: Create ExchangeBuy records in a currency ledger, buy a stock in that currency, verify the stock transaction's exchange rate matches the LIFO-weighted average, displayed as read-only.

### Implementation for User Story 1

- [ ] T007 [US1] Rewrite exchange rate calculation in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs` (replace lines 66-86) — for Buy transactions: (1) if TWD stock, rate=1.0; (2) otherwise, fetch ledger transactions via `currencyTransactionRepository.GetByLedgerIdOrderedAsync`, call `currencyLedgerService.CalculateExchangeRateForPurchase` for the LIFO rate; (3) if LIFO returns 0 (no cost layers), fall back to `txDateFxService.GetOrFetchAsync` for market rate; (4) if both fail, throw `BusinessRuleException` with message directing user to create exchange records. Remove all references to `request.ExchangeRate`.
- [ ] T008 [US1] Add `GET exchange-rate-preview` endpoint to `backend/src/InvestmentTracker.API/Controllers/CurrencyController.cs` — accepts ledgerId (path), amount (query decimal), date (query DateTime); validates ledger ownership; calls `CalculateExchangeRateForPurchase` for LIFO rate + `CalculateBalance` for balance check + `GetOrFetchAsync` for market rate; determines source ("lifo"/"market"/"blended"); returns `ExchangeRatePreviewResponse`. Return 422 if neither LIFO nor market rate available.
- [ ] T009 [US1] Modify exchange rate display in `frontend/src/components/transactions/TransactionForm.tsx` — replace the editable `<input type="number" name="exchangeRate">` (around lines 410-426) with a read-only display element showing the system-calculated rate. Add a `useEffect` or debounced callback that calls `getExchangeRatePreview` when `ticker`, `shares`, `pricePerShare`, or `transactionDate` change (and stock is non-TWD with a bound ledger). Show source label ("帳本成本" for lifo, "市場匯率" for market). Show loading spinner while fetching. Show error message with guidance if rate unavailable. Remove `exchangeRate` from form state and submission payload.

**Checkpoint**: User Story 1 fully functional — LIFO exchange rate auto-calculated and displayed read-only

---

## Phase 4: User Story 2 — Insufficient Balance Handling with Three Options (Priority: P1)

**Goal**: Replace the existing 2-button AutoDeposit modal with a 3-option dialog (Margin / Top Up / Cancel) when ledger balance is insufficient for a stock purchase.

**Independent Test**: Attempt a stock purchase with insufficient ledger balance; verify Margin creates the transaction with weighted blend rate and negative balance; verify Top Up creates a currency transaction of the user-selected type then proceeds; verify Cancel aborts.

### Implementation for User Story 2

- [ ] T010 [US2] Rewrite balance handling logic in `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs` (replace AutoDeposit block, lines 153-191) — after calculating exchange rate and checking balance: (1) if balance sufficient, proceed normally; (2) if balance insufficient and `BalanceAction.None`, throw `BusinessRuleException("帳本餘額不足，請選擇處理方式")`; (3) if `BalanceAction.Margin`, use `CalculateExchangeRateWithMargin` for weighted blend rate, proceed (balance goes negative); (4) if `BalanceAction.TopUp`, validate `TopUpTransactionType` is an income type, create currency transaction of the specified type with market rate (or rate=1.0 for TWD), recalculate LIFO rate including new transaction, then proceed. Link top-up transaction to stock transaction via `relatedStockTransactionId`. Update portfolio snapshot for the top-up transaction.
- [ ] T011 [US2] Replace AutoDeposit modal in `frontend/src/components/transactions/TransactionForm.tsx` — remove the existing `ConfirmationModal` for "帳本餘額不足" (lines 477-498) and `showAutoDepositModal` state. Create a new `InsufficientBalanceModal` (inline or component) with three options: (1) "融資" button — calls `executeSubmit` with `balanceAction: 1` (Margin); (2) "補足餘額" button — expands to show a transaction type dropdown (ExchangeBuy, Deposit, InitialBalance, Interest, OtherIncome) with the shortfall amount displayed, then on confirm calls `executeSubmit` with `balanceAction: 2, topUpTransactionType: selectedType`; (3) "取消" button — closes modal. Update `executeSubmit` function to include `balanceAction` and `topUpTransactionType` in the request payload instead of `autoDeposit`. Update the balance check logic (around lines 239-259) to show the new modal.

**Checkpoint**: User Stories 1 AND 2 fully functional — LIFO rate + 3-option balance handling

---

## Phase 5: User Story 3 — Ledger Navigation Remembers Last Selection (Priority: P2)

**Goal**: The "帳本" nav button navigates to the last-selected ledger instead of the generic `/ledger` route.

**Independent Test**: Select a specific ledger, navigate away, click "帳本" nav button — should return to the previously selected ledger.

### Implementation for User Story 3

- [ ] T012 [US3] Modify ledger navigation in `frontend/src/components/layout/Navigation.tsx` — in the desktop nav section (line 269), read `localStorage.getItem('selected_ledger_id')` and change `NavLink to` from `"/ledger"` to `` `/ledger/${savedLedgerId}` `` when a saved ID exists, fallback to `"/ledger"` when null. Apply the same change to the mobile nav section (around line 375) for `MobileNavLink`. The localStorage key `'selected_ledger_id'` is defined in `LedgerContext.tsx` line 24. Use a simple inline read — no React context hook needed.

**Checkpoint**: All user stories independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup and verification

- [ ] T013 Search for and remove any remaining references to old `AutoDeposit` field or `autoDeposit` property across backend and frontend codebase (grep for `AutoDeposit`, `autoDeposit`, `auto_deposit`). Clean up unused imports.
- [ ] T014 Build verification — ensure both backend (`dotnet build`) and frontend (`npm run build` / `npm run type-check`) compile without errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 completion
- **US2 (Phase 4)**: Depends on Phase 3 completion (US1 must be done first — LIFO rate logic must be in place before balance handling can use it)
- **US3 (Phase 5)**: Depends on Phase 2 completion — INDEPENDENT of US1/US2, can run in parallel with Phase 3-4 if staffed
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) ──→ Phase 2 (Foundational)
                         │
                         ├──→ Phase 3 (US1: LIFO Rate) ──→ Phase 4 (US2: Balance Handling)
                         │                                           │
                         └──→ Phase 5 (US3: Navigation) ────────────┤
                                                                     ↓
                                                              Phase 6 (Polish)
```

- **US1 → US2**: Sequential dependency — US2's balance handling (Margin weighted blend, TopUp with LIFO recalc) depends on US1's LIFO integration being in place
- **US3**: Fully independent — can run in parallel with US1/US2

### Parallel Opportunities

- **Phase 1**: T002 and T003 can run in parallel (different files)
- **Phase 2**: T005 and T006 can run in parallel (different files, both frontend)
- **Phase 3-5**: US3 can run in parallel with US1/US2 if assigned to a different engineer
- **Within US1**: T007 and T008 are both backend but different files — can be parallel. T009 depends on T008 (needs the preview API).

---

## Parallel Example: Setup + Foundational

```
# Phase 1 — Launch T002 and T003 in parallel:
Task: "Add ExchangeRatePreviewResponse DTO in Application/DTOs/"
Task: "Add CalculateExchangeRateWithMargin to CurrencyLedgerService"

# Phase 2 — Launch T005 and T006 in parallel:
Task: "Add BalanceAction TypeScript type to frontend/src/types/index.ts"
Task: "Add getExchangeRatePreview API function to frontend/src/api/"
```

## Parallel Example: US1 Backend

```
# Phase 3 — Launch T007 and T008 in parallel:
Task: "Rewrite exchange rate calc in CreateStockTransactionUseCase"
Task: "Add exchange-rate-preview endpoint to CurrencyController"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (3 tasks)
3. Complete Phase 3: US1 — LIFO Exchange Cost (3 tasks)
4. **STOP and VALIDATE**: Test LIFO rate calculation end-to-end
5. Proceed to US2 → US3 → Polish

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. US1 → LIFO rate working → **MVP achievable here**
3. US2 → 3-option balance handling → Core feature complete
4. US3 → Navigation fix → UX polish complete
5. Polish → Cleanup and verification

---

## Notes

- US1 and US2 both modify `CreateStockTransactionUseCase.cs` but different sections (exchange rate logic vs balance handling). Execute sequentially.
- US1 and US2 both modify `TransactionForm.tsx` but different parts (exchange rate field vs modal). Execute sequentially.
- US3 is the only fully independent story — can be assigned to a separate engineer.
- No database migrations needed — all changes operate on existing schema.
- The `ExchangeRate` and `AutoDeposit` fields are removed from the API request — frontend and backend must be updated in the same deployment.
