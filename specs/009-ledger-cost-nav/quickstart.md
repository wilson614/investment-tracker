# Quickstart: Ledger Exchange Cost Integration & Navigation Improvement

**Feature**: 009-ledger-cost-nav
**Date**: 2026-02-11

## Implementation Order

```
Phase 1: Backend Domain Layer
    → BalanceAction enum
    → CurrencyLedgerService.CalculateExchangeRateWithMargin
    → Unit tests for weighted blend

Phase 2: Backend Application Layer
    → Modify CreateStockTransactionRequest DTO
    → Modify CreateStockTransactionUseCase (LIFO integration + balance handling)
    → Add ExchangeRatePreviewResponse DTO
    → Unit tests for use case changes

Phase 3: Backend API Layer
    → Add exchange-rate-preview endpoint to CurrencyController
    → Update StockTransactionsController if needed

Phase 4: Frontend
    → Add BalanceAction type + preview API call
    → Modify TransactionForm.tsx (read-only rate + 3-option modal)
    → Fix Navigation.tsx ledger button
```

## Key Files to Modify

### Backend

| File | What to Change |
|------|---------------|
| `Domain/Enums/BalanceAction.cs` | **NEW** — Create enum (None, Margin, TopUp) |
| `Domain/Services/CurrencyLedgerService.cs` | Add `CalculateExchangeRateWithMargin()` method |
| `Application/DTOs/RequestDtos.cs` | Replace `ExchangeRate` + `AutoDeposit` with `BalanceAction` + `TopUpTransactionType` |
| `Application/DTOs/ResponseDtos.cs` | Add `ExchangeRatePreviewResponse` record |
| `Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs` | Major: replace exchange rate logic (lines 66-86), replace AutoDeposit logic (lines 153-191) |
| `API/Controllers/CurrencyController.cs` | Add `GET exchange-rate-preview` endpoint |

### Frontend

| File | What to Change |
|------|---------------|
| `src/types/index.ts` | Add `BalanceAction` enum type |
| `src/api/` | Add `getExchangeRatePreview()` API function |
| `src/components/transactions/TransactionForm.tsx` | Replace exchange rate input with read-only display; replace AutoDeposit modal with 3-option modal |
| `src/components/layout/Navigation.tsx` | Read `selected_ledger_id` from localStorage for ledger nav link |

### Tests

| File | What to Change |
|------|---------------|
| `Domain.Tests/Services/CurrencyLedgerServiceTests.cs` | Add tests for `CalculateExchangeRateWithMargin` |
| `Application.Tests/UseCases/StockTransactions/CreateStockTransactionUseCaseTests.cs` | **NEW** — Tests for LIFO integration, balance actions |

## Critical Implementation Notes

1. **Exchange rate calculation order**: In `CreateStockTransactionUseCase`, the LIFO rate must be calculated BEFORE checking balance sufficiency. The balance check determines which modal option to present, but the rate is needed regardless.

2. **Weighted blend formula**: `blendedRate = (coveredAmount × lifoRate + marginAmount × marketRate) / totalAmount`. Use `decimal` arithmetic throughout. Round final result to 6 decimal places (matching existing `SetExchangeRate` precision).

3. **Top Up atomicity**: When `BalanceAction = TopUp`, create the currency transaction FIRST, then recalculate the LIFO rate (which now includes the new transaction), then create the stock transaction with the updated rate. All within the same use case invocation.

4. **Preview vs actual rate**: The preview endpoint calculates the rate without creating any transactions. The actual creation recalculates to ensure consistency. The rates should match unless concurrent modifications occur.

5. **Backward compatibility**: The `AutoDeposit` field is removed from the API. Frontend must be updated simultaneously. No migration needed for existing transactions — they retain their original exchange rates.

6. **Navigation fix**: Use `localStorage.getItem('selected_ledger_id')` directly in render, not via React context. The localStorage key `'selected_ledger_id'` is defined in `LedgerContext.tsx` line 24.
