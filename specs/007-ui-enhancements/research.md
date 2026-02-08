# Research: UI Enhancements Batch

**Feature**: 007-ui-enhancements
**Date**: 2026-02-08

## 1. Stock Trading Dialog Flicker Fix

### Decision
Remove the "Linked Ledger" section entirely from TransactionForm and compact the notes field to a single-line input.

### Rationale
- The Linked Ledger section causes re-renders when portfolio data loads asynchronously
- This feature is a "transaction record" system, not a live trading platform
- Removing the section eliminates the root cause of flicker rather than masking it
- Compacting notes field reduces dialog height to fit standard 1080p screens

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| Memoize Linked Ledger section | Still causes layout shift during initial load |
| Use skeleton loader | Adds complexity, doesn't solve content overflow issue |
| Multi-step wizard | Over-engineering for a simple form |

### Implementation Pattern
- Remove Linked Ledger section code (lines ~461-500 in TransactionForm.tsx)
- Change notes field from textarea to single-line input
- Verify dialog fits in viewport without scrolling on 1080p

---

## 2. Ledger Dropdown Navigation

### Decision
Create a LedgerContext similar to PortfolioContext and LedgerSelector component based on PortfolioSelector.

### Rationale
- Follows established patterns in the codebase (PortfolioContext, PortfolioSelector)
- Provides consistent UX across portfolio and ledger navigation
- Uses localStorage for session persistence (key: `selected_ledger_id`)
- Eliminates the need for a separate overview page

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| URL-based state | Requires route changes, more complex |
| Redux/Zustand | Overkill for simple selection state |
| Keep overview page with quick links | Doesn't achieve the desired UX simplification |

### Implementation Pattern
Reference: `frontend/src/components/portfolio/PortfolioSelector.tsx`
- Create `LedgerContext.tsx` with `currentLedgerId`, `selectLedger()`, `ledgers` state
- Create `LedgerSelector.tsx` mirroring PortfolioSelector UI
- Refactor Currency.tsx to redirect to CurrencyDetail with context
- Store selection in localStorage: `selected_ledger_id`

---

## 3. Bank Account Export/Import

### Decision
Use CSV format following existing patterns in `csvExport.ts` and `CSVImportModal`.

### Rationale
- Consistent with existing stock and ledger transaction export/import
- CSV is user-friendly and editable in Excel/Google Sheets
- Frontend-driven parsing with backend single-record API (existing pattern)
- Include version header for future compatibility

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| JSON format | Less user-friendly for manual editing |
| Backend batch API | Adds complexity; existing single-record pattern works |
| Both JSON and CSV | Scope creep; CSV covers the use case |

### CSV Format Design
```csv
# InvestmentTracker Bank Accounts Export v1.0
BankName,TotalAssets,InterestRate,InterestCap,Currency,Note,IsActive
"台新銀行",1500000,0.0185,50000,TWD,"活存帳戶",true
"永豐銀行",500000,0.0200,30000,TWD,"定存",true
```

### Implementation Pattern
Reference: `frontend/src/services/csvExport.ts`, `frontend/src/components/import/StockImportButton.tsx`

**Export Flow**:
1. Fetch all bank accounts from API
2. Format to CSV with UTF-8 BOM (for Excel)
3. Trigger download via blob URL

**Import Flow**:
1. User selects CSV file
2. Parse CSV, validate format
3. Preview with duplicate detection (by BankName)
4. On confirm, call existing create/update API per record

---

## Summary

All research items resolved. No NEEDS CLARIFICATION remains.

| Item | Status | Pattern Reference |
|------|--------|-------------------|
| Dialog flicker fix | ✅ Resolved | Direct removal of problematic section |
| Ledger navigation | ✅ Resolved | PortfolioContext/PortfolioSelector pattern |
| Bank account export/import | ✅ Resolved | csvExport.ts, CSVImportModal pattern |
