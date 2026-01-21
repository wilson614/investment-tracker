# Data Model: UX Enhancements & Market Selection

**Date**: 2026-01-21
**Feature**: 003-ux-enhancements

## Entity Changes Overview

This module extends the existing data model with:
1. **StockTransaction** - Add Market field (modified)
2. **StockSplit** - New entity for stock split records
3. **UserBenchmark** - New entity for per-user benchmark selections

---

## §1 StockTransaction (Modified)

**Change**: Add `Market` field to persist market selection.

### Current Schema
```csharp
public class StockTransaction
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public string Ticker { get; set; }
    public DateTime TransactionDate { get; set; }
    public TransactionType TransactionType { get; set; }
    public decimal Shares { get; set; }
    public decimal PricePerShare { get; set; }
    public string Currency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal Fees { get; set; }
    // ... other fields
}
```

### Updated Schema
```csharp
public class StockTransaction
{
    // ... existing fields ...

    public StockMarket Market { get; set; }  // NEW: TW=0, US=1, UK=2, EU=3
}

public enum StockMarket
{
    TW = 0,
    US = 1,
    UK = 2,
    EU = 3
}
```

### Migration
```sql
-- Add Market column with default US
ALTER TABLE stock_transactions ADD COLUMN market INTEGER NOT NULL DEFAULT 1;

-- Populate based on ticker pattern
UPDATE stock_transactions SET market =
  CASE
    WHEN ticker ~ '^[0-9]+[A-Za-z]*$' THEN 0  -- TW (numeric prefix)
    WHEN ticker LIKE '%.L' THEN 2              -- UK (ends with .L)
    ELSE 1                                      -- US (default)
  END;
```

### Business Rules
- Migration auto-populates based on `guessMarket()` logic
- Users can edit transaction to correct market if auto-guess was wrong
- Quote fetching uses market from latest transaction for each ticker

---

## §2 StockSplit (New)

**Purpose**: Store stock split records for share count adjustment calculations.

### Schema
```csharp
public class StockSplit
{
    public Guid Id { get; set; }
    public string Ticker { get; set; }
    public DateTime SplitDate { get; set; }
    public decimal SplitRatio { get; set; }  // e.g., 2.0 for 2:1 split
    public string? Description { get; set; } // Optional notes
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Database Table
```sql
CREATE TABLE stock_splits (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticker VARCHAR(20) NOT NULL,
    split_date DATE NOT NULL,
    split_ratio DECIMAL(10,4) NOT NULL,
    description VARCHAR(255) NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_splits_ticker ON stock_splits(ticker);
CREATE INDEX idx_stock_splits_split_date ON stock_splits(split_date);
CREATE UNIQUE INDEX idx_stock_splits_ticker_date ON stock_splits(ticker, split_date);
```

### Business Rules
- **Global scope**: Shared across all users (not per-user)
- **Split ratio interpretation**:
  - 2.0 = 2:1 split (each share becomes 2 shares)
  - 0.5 = 1:2 reverse split (each 2 shares become 1 share)
- **Application timing**: Applied to transactions with date < split_date
- **Calculation**: `adjustedShares = originalShares * splitRatio`

### Examples
```
| Ticker | SplitDate   | SplitRatio | Description      |
|--------|-------------|------------|------------------|
| NVDA   | 2024-06-10  | 10.0       | 10-for-1 split   |
| TSLA   | 2022-08-25  | 3.0        | 3-for-1 split    |
| GOOGL  | 2022-07-18  | 20.0       | 20-for-1 split   |
```

---

## §3 UserBenchmark (New)

**Purpose**: Store per-user custom benchmark stock selections for performance comparison.

### Schema
```csharp
public class UserBenchmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Ticker { get; set; }
    public StockMarket Market { get; set; }
    public string? DisplayName { get; set; }  // User-friendly name
    public DateTime AddedAt { get; set; }

    // Navigation
    public User User { get; set; }
}
```

### Database Table
```sql
CREATE TABLE user_benchmarks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    ticker VARCHAR(20) NOT NULL,
    market INTEGER NOT NULL,
    display_name VARCHAR(100) NULL,
    added_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_user_benchmarks_user_id ON user_benchmarks(user_id);
CREATE UNIQUE INDEX idx_user_benchmarks_user_ticker ON user_benchmarks(user_id, ticker, market);
```

### Business Rules
- **Per-user storage**: Each user has their own benchmark list
- **No price storage**: Prices fetched on-demand via existing Sina/Stooq APIs
- **Market required**: Must specify market for accurate API routing
- **Recommended limit**: 5 benchmarks for chart readability (soft limit, not enforced)

---

## §4 Relationships Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         User                                     │
│  (unchanged from 001-portfolio-tracker)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              │ 1:1           │ 1:N           │ 1:N
              ▼               ▼               ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│   Portfolio     │  │  UserBenchmark  │  │  (other user    │
│   (unchanged)   │  │     [NEW]       │  │   entities)     │
└─────────────────┘  │  UserId, Ticker │  └─────────────────┘
        │            │  Market, Name   │
        │ 1:N        └─────────────────┘
        ▼
┌─────────────────────────────────────────────────────────────────┐
│                   StockTransaction                               │
│  [MODIFIED] + Market: StockMarket (enum)                        │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                      StockSplit                                  │
│  [NEW] Standalone global table - not per-user                   │
│  Ticker, SplitDate, SplitRatio, Description                     │
└─────────────────────────────────────────────────────────────────┘
```

---

## §5 Migration Strategy

### Migration Order
1. Add `market` column to `stock_transactions` with default value
2. Update existing transactions with guessed market values
3. Create `stock_splits` table
4. Create `user_benchmarks` table

### EF Core Migration Commands
```bash
cd backend/src/InvestmentTracker.Infrastructure
dotnet ef migrations add AddMarketToTransaction -s ../InvestmentTracker.API
dotnet ef migrations add CreateStockSplitTable -s ../InvestmentTracker.API
dotnet ef migrations add CreateUserBenchmarkTable -s ../InvestmentTracker.API
dotnet ef database update -s ../InvestmentTracker.API
```

### Backward Compatibility
- Existing code: Will receive default `Market = US` for old transactions
- Market guessing logic: Kept as fallback for UI auto-fill
- No breaking changes to existing API contracts

### Rollback Plan
- Migration 1: Drop `market` column (safe)
- Migration 2-3: Drop new tables (safe)
- All migrations reversible

---

## §6 Validation Rules

### StockTransaction.Market
- Required field (NOT NULL)
- Must be valid enum value (0-3)
- Validated at API boundary

### StockSplit
- Ticker: Required, max 20 chars
- SplitDate: Required, valid date
- SplitRatio: Required, must be > 0
- Unique constraint: (Ticker, SplitDate)

### UserBenchmark
- UserId: Required, must exist
- Ticker: Required, max 20 chars
- Market: Required, valid enum
- Unique constraint: (UserId, Ticker, Market)
