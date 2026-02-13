# Data Model: Closed-Loop Performance Model & Transaction Type Redesign

**Feature**: 011-closed-loop-performance-model
**Date**: 2026-02-13

## Overview

This feature does not require introducing a brand-new persistence domain. It restructures existing transaction semantics and derived performance calculations by tightening model meaning, validation, and CF derivation.

## Entity 1: CurrencyTransactionCategory (Enum Semantics)

### Purpose
Defines canonical business meaning for ledger events and drives:
- create/update validation,
- CSV import validation and diagnostics,
- return CF inclusion/exclusion,
- UI labels and CSV mapping.

### Core semantic groups

| Group | Meaning | CF Policy |
|---|---|---|
| Explicit External Inflow | Capital entering closed loop | Included |
| Explicit External Outflow | Capital leaving closed loop | Included |
| Internal Reallocation | Capital movement within closed loop | Excluded |
| Internal Return Component | Return generated inside system | Excluded |

### Required semantic transitions from current model

| Current | Target Semantics |
|---|---|
| `InitialBalance` | `TransferInBalance` explicit external inflow |
| Stock-linked use of generic `OtherIncome`/`OtherExpense` | Dedicated stock-linked internal categories |
| Mixed-income usage | `OtherIncome` stays single explicit external inflow category |
| `OtherExpense` ambiguous use | `OtherExpense` explicit external outflow only |
| Dividend mixed with other income/interest | Dedicated dividend category |

## Entity 2: LedgerTransactionValidationRule

### Purpose
Policy matrix enforcing allowed transaction categories per ledger currency context.

### Inputs
- `ledgerCurrency` (e.g., TWD / non-TWD)
- `transactionCategory`
- `foreignAmount`
- `homeAmount`
- `exchangeRate`

### Outputs
- `isValid`
- `errorCode`
- `errorMessage`
- `correctionGuidance`

### Invariants
1. Validation is enforced in backend create/update regardless of frontend behavior.
2. Validation behavior for CSV must be identical to API manual entry.
3. No legacy alias acceptance for deprecated enum names.

## Entity 3: CurrencyCsvImportBatch

### Purpose
Represents one currency CSV import request with atomic commit behavior.

### Fields
| Field | Type | Description |
|---|---|---|
| `batchId` | Guid/string | Request correlation id |
| `ledgerId` | Guid | Target ledger |
| `rows` | Collection<Row> | Parsed CSV rows |
| `status` | Enum | `Rejected` / `Committed` |
| `diagnostics` | Collection<RowDiagnostic> | Full validation result set |

### State transitions
```text
Received -> Parsed -> Validated
Validated -> Rejected (if any invalid row)
Validated -> Committed (if all rows valid)
```

### Invariants
1. If any row invalid, committed row count must be 0.
2. Rejection response returns complete diagnostics (not first error only).

## Entity 4: CurrencyCsvRowDiagnostic

### Purpose
Machine- and user-readable error unit returned for rejected imports.

### Fields
| Field | Type | Description |
|---|---|---|
| `rowNumber` | int | 1-based data row position |
| `fieldName` | string | Invalid field |
| `invalidValue` | string | Raw invalid value |
| `errorCode` | string | Stable code for testing |
| `message` | string | Human-readable error |
| `correctionGuidance` | string | How to fix and re-import |

## Entity 5: ClosedLoopValuationSnapshot

### Purpose
Canonical valuation point used by both MD and TWR.

### Formula
`V_t = StockMarketValue_t + BoundLedgerBalance_t`

### Fields
| Field | Type | Description |
|---|---|---|
| `timestamp/date` | Date | Valuation point |
| `stockMarketValue` | decimal | Sum of marked-to-market positions |
| `boundLedgerBalance` | decimal | Ledger balance (can be negative) |
| `totalNetAssetValue` | decimal | Closed-loop sum |

### Invariants
1. `boundLedgerBalance` can be negative; never clamp to zero.
2. `totalNetAssetValue` is used consistently by both MD and TWR pipelines.

## Entity 6: ReturnCashFlowEvent

### Purpose
Derived event set for Modified Dietz and TWR cash-flow inputs.

### Fields
| Field | Type | Description |
|---|---|---|
| `eventDate` | Date | Event date |
| `amount` | decimal | Signed CF amount |
| `category` | Enum | Transaction category |
| `isExplicitExternal` | bool | Whether event is explicit external flow |
| `sourceTransactionId` | Guid | Traceability |

### Inclusion rules
Included only when category is explicit external in/out per policy:
- include `TransferInBalance`
- include `Deposit` / `Withdraw`
- include `OtherIncome` / `OtherExpense`
- include non-TWD `ExchangeBuy` (positive)
- include non-TWD `ExchangeSell` (negative)

Excluded:
- stock buy/sell-linked internal events
- internal FX effects
- internal return categories (interest/dividend)

## Entity 7: MetricHelpDefinition

### Purpose
Frontend copy contract for metric interpretation.

### Required values
| Metric | Required Text |
|---|---|
| Modified Dietz | `衡量比例的重壓 (Modified Dietz)` |
| TWR | `衡量本金的重壓 (TWR)` |

## Relationships

```text
CurrencyTransactionCategory
  -> drives LedgerTransactionValidationRule
  -> drives ReturnCashFlowEvent inclusion
  -> drives frontend labels/import/export mapping

CurrencyCsvImportBatch
  -> contains CurrencyCsvRowDiagnostic[*]
  -> validated by LedgerTransactionValidationRule

ClosedLoopValuationSnapshot
  -> consumed by Modified Dietz calculation
  -> consumed by TWR calculation

ReturnCashFlowEvent
  -> generated from CurrencyTransaction + policy
  -> consumed by Modified Dietz and TWR
```
