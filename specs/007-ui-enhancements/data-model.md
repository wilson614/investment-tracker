# Data Model: UI Enhancements Batch

**Feature**: 007-ui-enhancements
**Date**: 2026-02-08

## Overview

This feature batch is primarily UI-focused and does not introduce new database entities. It leverages existing entities and adds client-side state management.

---

## Existing Entities (No Changes)

### BankAccount

**Location**: `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Auto-generated |
| UserId | Guid | FK, Required | Multi-tenancy filter |
| BankName | string | Required, MaxLength(100) | Used for duplicate detection |
| TotalAssets | decimal | Required, >= 0 | Precision: 18,2 |
| InterestRate | decimal | Required, >= 0 | Precision: 18,4 (4 decimal places) |
| InterestCap | decimal? | Nullable, >= 0 | Maximum interest earning limit |
| Currency | string | Required, Default: "TWD" | ISO currency code |
| Note | string? | MaxLength(500) | Optional remarks |
| IsActive | bool | Required, Default: true | Soft status flag |
| CreatedAt | DateTime | Required | Audit field |
| UpdatedAt | DateTime | Required | Audit field |

**Export/Import Mapping**:
- All fields except Id, UserId, CreatedAt, UpdatedAt are exported
- On import, UserId is set from current user context
- Duplicate detection uses BankName (case-insensitive match)

---

### CurrencyLedger

**Location**: `backend/src/InvestmentTracker.Domain/Entities/CurrencyLedger.cs`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Auto-generated |
| UserId | Guid | FK, Required | Multi-tenancy filter |
| Currency | string | Required | ISO currency code |
| PortfolioId | Guid | FK, Required | Associated portfolio |

**No changes required** - used for dropdown list population.

---

## New Client-Side State

### LedgerContext

**Location**: `frontend/src/contexts/LedgerContext.tsx`

```typescript
interface LedgerContextValue {
  ledgers: CurrencyLedger[];
  currentLedgerId: string | null;
  isLoading: boolean;
  selectLedger: (id: string) => void;
}
```

**Persistence**:
- Key: `selected_ledger_id`
- Storage: localStorage
- Behavior: Restored on page load, cleared if ledger no longer exists

---

## Validation Rules

### Bank Account Import Validation

| Rule | Error Message |
|------|---------------|
| BankName is empty | "Bank name is required" |
| BankName exceeds 100 chars | "Bank name must be 100 characters or less" |
| TotalAssets is negative | "Total assets cannot be negative" |
| InterestRate is negative | "Interest rate cannot be negative" |
| InterestRate > 1 | "Interest rate appears invalid (should be decimal, e.g., 0.02 for 2%)" |
| Currency not recognized | "Unsupported currency: {value}" |
| CSV format invalid | "Invalid CSV format. Expected columns: BankName, TotalAssets, ..." |

---

## State Transitions

### Import Preview States

```
[Initial] → [File Selected] → [Parsing] → [Preview Ready] → [Importing] → [Complete]
                                  ↓              ↓               ↓
                              [Parse Error]  [Cancel]      [Import Error]
```

| State | User Action | Next State |
|-------|-------------|------------|
| Initial | Click "Import" | File Selected |
| File Selected | Choose file | Parsing |
| Parsing | (automatic) | Preview Ready or Parse Error |
| Preview Ready | Click "Confirm" | Importing |
| Preview Ready | Click "Cancel" | Initial |
| Importing | (automatic) | Complete or Import Error |
| Complete | (automatic close) | Initial |

---

## Relationships

```
User (1) ─────┬───── (*) BankAccount
              │
              └───── (*) CurrencyLedger ───── (1) Portfolio
```

No new relationships introduced in this feature.
