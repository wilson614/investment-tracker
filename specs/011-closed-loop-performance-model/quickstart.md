# Quickstart: Closed-Loop Performance Model & Transaction Type Redesign

**Feature**: 011-closed-loop-performance-model
**Branch**: `011-closed-loop-performance-model`

## Prerequisites

- .NET 8 SDK
- Node.js 18+
- PostgreSQL
- Existing project runs successfully before this feature branch changes

## 1) Backend work areas

### A. Transaction category semantics and validation
- `backend/src/InvestmentTracker.Domain/Enums/CurrencyTransactionType.cs`
- `backend/src/InvestmentTracker.Domain/Entities/CurrencyTransaction.cs`
- `backend/src/InvestmentTracker.Application/DTOs/CurrencyLedgerDtos.cs`
- `backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/CreateCurrencyTransactionUseCase.cs`
- `backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/UpdateCurrencyTransactionUseCase.cs`
- `backend/src/InvestmentTracker.Application/Validators/CreateCurrencyTransactionRequestValidator.cs`
- (add/update update-request validator as needed)

### B. CSV import atomic endpoint
- `backend/src/InvestmentTracker.API/Controllers/CurrencyTransactionsController.cs`
- new import use case/services under `InvestmentTracker.Application/UseCases/CurrencyTransactions/`

Target behavior:
- same ledger-currency/type validation matrix as manual create/update
- all-or-nothing commit
- 422 with full diagnostics set when invalid

### C. Closed-loop annual performance and CF policy
- `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- `backend/src/InvestmentTracker.Infrastructure/Services/TransactionPortfolioSnapshotService.cs`
- `backend/src/InvestmentTracker.Domain/Services/ReturnCashFlowStrategy.cs`
- `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/StockTransactionLinking.cs`

Target behavior:
- no ledger floor-to-zero in valuation
- MD and TWR use same closed-loop baseline
- explicit external-only CF inclusion with non-TWD exchange flow rules

## 2) Frontend work areas

- `frontend/src/components/currency/CurrencyTransactionForm.tsx`
- `frontend/src/components/transactions/TransactionForm.tsx`
- `frontend/src/pages/CurrencyDetail.tsx`
- `frontend/src/components/import/CurrencyImportButton.tsx`
- `frontend/src/services/csvExport.ts`
- `frontend/src/pages/Performance.tsx`
- `frontend/src/types/index.ts`

Target behavior:
- labels/mappings aligned with redesigned enum semantics
- MD/TWR help text updated to required wording
- import parser terms synchronized with backend contract

## 3) Test focus (minimum regression set)

### Backend
- currency transaction validation matrix tests (TWD vs non-TWD)
- create/update controller/use case validation tests
- CSV import all-or-nothing + complete diagnostics tests
- performance regression: negative ledger included in valuation
- CF policy regression: explicit external-only inclusion and non-TWD exchange rules

### Frontend
- update tests for metric wording where asserted
- add/update tests for transaction label mapping and import parser behavior

## 4) Run validation commands

```bash
# backend tests
bash -lc "dotnet test /workspaces/InvestmentTracker/tests"

# frontend checks/tests
bash -lc "npm --prefix /workspaces/InvestmentTracker/frontend run type-check"
bash -lc "npm --prefix /workspaces/InvestmentTracker/frontend run test"
```

## 5) Expected completion checklist

- [ ] Enum semantics and naming updated across backend/frontend
- [ ] Backend create/update strict validation implemented
- [ ] CSV import endpoint supports all-or-nothing + full diagnostics
- [ ] Closed-loop valuation baseline unified for MD/TWR
- [ ] Negative ledger is not floored
- [ ] CF policy aligned to explicit external events including non-TWD exchange rules
- [ ] MD/TWR help text wording updated
- [ ] Regression tests added/updated and passing
