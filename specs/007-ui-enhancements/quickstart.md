# Quickstart: UI Enhancements Batch

**Feature**: 007-ui-enhancements
**Date**: 2026-02-08

## Prerequisites

- Node.js 18+
- .NET 8 SDK
- PostgreSQL running (via Docker or local)
- Git on branch `007-ui-enhancements`

## Development Setup

### 1. Start Backend

```bash
cd backend
dotnet restore
dotnet run --project src/InvestmentTracker.Api
# API available at http://localhost:5000
```

### 2. Start Frontend

```bash
cd frontend
npm install
npm run dev
# Dev server at http://localhost:3000
```

## Feature Implementation Order

### Feature 1: Stock Trading Dialog Fix (P1)

**Files to Modify**:
- `frontend/src/components/transactions/TransactionForm.tsx`

**Steps**:
1. Remove the Linked Ledger section (approximately lines 461-500)
2. Change notes field from `<textarea>` to `<input type="text">`
3. Verify dialog fits viewport on 1080p without scrolling

**Test**:
```bash
# Manual test
1. Navigate to Portfolio page
2. Click "Add Transaction"
3. Verify: No flickering, dialog fits screen, Linked Ledger section gone
```

---

### Feature 2: Ledger Dropdown Navigation (P2)

**Files to Create**:
- `frontend/src/contexts/LedgerContext.tsx`
- `frontend/src/components/ledger/LedgerSelector.tsx`

**Files to Modify**:
- `frontend/src/pages/Currency.tsx`
- `frontend/src/pages/CurrencyDetail.tsx`
- `frontend/src/App.tsx` (add LedgerProvider)

**Steps**:
1. Create LedgerContext following PortfolioContext pattern
2. Create LedgerSelector following PortfolioSelector pattern
3. Wrap app with LedgerProvider
4. Modify Currency.tsx to auto-redirect to last selected ledger
5. Add LedgerSelector to CurrencyDetail.tsx header

**Reference Files**:
- `frontend/src/contexts/PortfolioContext.tsx`
- `frontend/src/components/portfolio/PortfolioSelector.tsx`

**Test**:
```bash
# Manual test
1. Navigate to Ledger section
2. Verify: Dropdown shows in top-left
3. Select different ledger, verify instant switch
4. Navigate away and back, verify selection remembered
```

---

### Feature 3: Bank Account Export/Import (P3)

**Files to Create**:
- `frontend/src/components/import/BankAccountImportButton.tsx`
- `frontend/src/components/import/BankAccountImportModal.tsx`

**Files to Modify**:
- `frontend/src/services/csvExport.ts` (add bank account export)
- `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx` (add buttons)
- `backend/src/InvestmentTracker.Api/Controllers/BankAccountsController.cs`

**Backend Steps**:
1. Add `ExportAsync` action returning CSV
2. Add `ImportAsync` action with preview/execute modes
3. Add DTOs for import request/response

**Frontend Steps**:
1. Add `exportBankAccountsToCSV` function to csvExport.ts
2. Create BankAccountImportButton (reference: StockImportButton)
3. Create BankAccountImportModal (reference: CSVImportModal)
4. Add Export/Import buttons to BankAccountsPage header

**Reference Files**:
- `frontend/src/services/csvExport.ts`
- `frontend/src/components/import/StockImportButton.tsx`
- `frontend/src/components/import/CSVImportModal.tsx`

**Test**:
```bash
# Manual test
1. Navigate to Bank Accounts page
2. Click Export, verify CSV downloads
3. Modify CSV, click Import
4. Verify preview shows correctly
5. Confirm import, verify data updated
```

---

## Verification Checklist

| Feature | Test Command | Expected Result |
|---------|--------------|-----------------|
| Dialog fix | Open Add Transaction | No flicker, fits viewport |
| Ledger dropdown | Navigate to Ledgers | Dropdown visible, selection persists |
| Export | Click Export | CSV file downloads |
| Import | Upload CSV | Preview shown, data imported |

## Build Verification

```bash
# Frontend type check
cd frontend && npm run type-check

# Backend build
cd backend && dotnet build

# Run tests
cd frontend && npm test
cd backend && dotnet test
```
