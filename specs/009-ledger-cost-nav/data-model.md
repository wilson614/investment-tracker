# Data Model: Ledger Exchange Cost Integration & Navigation Improvement

**Feature**: 009-ledger-cost-nav
**Date**: 2026-02-11

## Overview

This feature requires **no new database tables or columns**. All changes operate on existing entities. One new enum is added to the Domain layer.

## Existing Entities (No Schema Changes)

### CurrencyTransaction

**File**: `backend/src/InvestmentTracker.Domain/Entities/CurrencyTransaction.cs`

| Field | Type | Description | Impact |
|-------|------|-------------|--------|
| `CurrencyLedgerId` | `Guid` | FK to CurrencyLedger | No change |
| `TransactionDate` | `DateTime` | Transaction date | No change |
| `TransactionType` | `CurrencyTransactionType` | Type enum (1-9) | No change — AutoDeposit will now create type `ExchangeBuy` (1) instead of `Deposit` (8) |
| `ForeignAmount` | `decimal` | Amount in foreign currency | No change |
| `HomeAmount` | `decimal?` | Amount in home currency | No change — will now be populated for top-up ExchangeBuy |
| `ExchangeRate` | `decimal?` | Exchange rate used | No change — will now be populated for top-up ExchangeBuy |
| `RelatedStockTransactionId` | `Guid?` | FK to triggering stock tx | No change — top-up transactions still linked |
| `Notes` | `string?` | Transaction notes | No change |

### StockTransaction

**File**: `backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs`

| Field | Type | Description | Impact |
|-------|------|-------------|--------|
| `ExchangeRate` | `decimal?` | Exchange rate for the transaction | No schema change — value source changes from user input/market API to LIFO calculation |

### CurrencyLedger

No changes. Balance may go negative when margin is used, but the entity already supports this (balance is calculated, not stored as a column).

## New Enum

### BalanceAction

**File**: `backend/src/InvestmentTracker.Domain/Enums/BalanceAction.cs` (NEW)

```csharp
/// <summary>
/// Specifies how to handle insufficient currency ledger balance during a stock purchase.
/// </summary>
public enum BalanceAction
{
    /// <summary>Default: no special handling. Transaction is rejected if balance insufficient.</summary>
    None = 0,

    /// <summary>Allow negative balance (margin). Proceed with purchase, balance goes negative.</summary>
    Margin = 1,

    /// <summary>Create a currency transaction to cover the shortfall before proceeding.</summary>
    TopUp = 2
}
```

## DTO Changes

### CreateStockTransactionRequest (Modified)

**File**: `backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs`

| Field | Change | Before | After |
|-------|--------|--------|-------|
| `ExchangeRate` | Remove | `decimal?` optional input | Removed from request — system-calculated |
| `AutoDeposit` | Replace | `bool` | Removed |
| `BalanceAction` | Add | — | `BalanceAction` enum (default: `None`) |
| `TopUpTransactionType` | Add | — | `CurrencyTransactionType?` (only used when `BalanceAction = TopUp`) |

### ExchangeRatePreviewResponse (New DTO)

**File**: `backend/src/InvestmentTracker.Application/DTOs/ResponseDtos.cs` or similar

```csharp
public record ExchangeRatePreviewResponse
{
    public decimal Rate { get; init; }
    public string Source { get; init; } = string.Empty;  // "lifo", "market", "blended"
    public decimal? LifoRate { get; init; }
    public decimal? MarketRate { get; init; }
    public decimal? LifoPortion { get; init; }     // Amount covered by LIFO
    public decimal? MarketPortion { get; init; }   // Amount on margin (market rate)
}
```

## State Transitions

### Stock Transaction Creation Flow (Updated)

```
User fills form (ticker, shares, price, date)
    │
    ├─ [TWD stock] → ExchangeRate = 1.0 → Create transaction
    │
    └─ [Foreign currency stock]
        │
        ├─ Call preview API → Get LIFO rate (displayed read-only)
        │
        ├─ [Balance sufficient] → Use LIFO rate → Create transaction
        │
        └─ [Balance insufficient] → Show 3-option modal
            │
            ├─ [Margin] → Weighted blend (LIFO + market) → Create transaction
            │               Balance goes negative
            │
            ├─ [Top Up] → User selects tx type → Create currency tx
            │              → Recalculate LIFO → Create stock transaction
            │
            └─ [Cancel] → Return to form
```

## Validation Rules

- `BalanceAction.TopUp` requires `TopUpTransactionType` to be set and must be an income type (ExchangeBuy, Deposit, InitialBalance, Interest, OtherIncome).
- `BalanceAction.Margin` does not require `TopUpTransactionType`.
- When `BalanceAction.None` and balance is insufficient, the use case throws `BusinessRuleException`.
- Exchange rate on StockTransaction must always be > 0 after calculation (enforced by existing `SetExchangeRate` method).
