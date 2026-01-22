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

---

## Phase 2 Entity Changes (US9-US17)

## §7 Currency Enum (New)

**Purpose**: Enumeration for transaction denomination currencies.

### Definition
```csharp
public enum Currency
{
    TWD = 1,  // Taiwan Dollar
    USD = 2,  // US Dollar
    GBP = 3,  // British Pound
    EUR = 4   // Euro
}
```

### Auto-Detection Rules
- Taiwan stocks (market=TW) → TWD
- All other markets → USD (user can override to GBP/EUR)

---

## §8 StockTransaction.Currency (Modified)

**Change**: Add `Currency` field to `StockTransaction` entity.

### Updated Schema
```csharp
public class StockTransaction
{
    // ... existing fields ...

    public StockMarket Market { get; set; }    // Added in Phase 1
    public Currency Currency { get; set; }     // NEW: TWD=1, USD=2, GBP=3, EUR=4
}
```

### Migration
```sql
-- Add Currency column with default USD
ALTER TABLE stock_transactions ADD COLUMN currency INTEGER NOT NULL DEFAULT 2;

-- Populate based on Market field
UPDATE stock_transactions SET currency =
  CASE
    WHEN market = 0 THEN 1  -- TW market → TWD
    ELSE 2                   -- All others → USD
  END;
```

---

## §9 UserPreferences (New)

**Purpose**: Store per-user UI preferences in database instead of localStorage.

### Schema
```csharp
public class UserPreferences : BaseEntity
{
    public Guid UserId { get; private set; }

    // YTD benchmark selections (JSON array, e.g., ["SPY", "VTI"])
    public string? YtdBenchmarkPreferences { get; private set; }

    // CAPE region selections (JSON array, e.g., ["US", "TW"])
    public string? CapeRegionPreferences { get; private set; }

    // Default portfolio ID
    public Guid? DefaultPortfolioId { get; private set; }

    // Navigation
    public User User { get; private set; }
    public Portfolio? DefaultPortfolio { get; private set; }
}
```

### Database Table
```sql
CREATE TABLE user_preferences (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    ytd_benchmark_preferences TEXT NULL,
    cape_region_preferences TEXT NULL,
    default_portfolio_id UUID NULL REFERENCES portfolios(id),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_user_preferences_user_id ON user_preferences(user_id);
```

### Business Rules
- **1:1 relationship**: One UserPreferences per User
- **Lazy creation**: Created on first preference save
- **JSON storage**: Preferences stored as JSON strings for flexibility

---

## §10 EuronextSymbolMapping (New)

**Purpose**: Cache Euronext ticker → ISIN/MIC mappings for quote fetching.

### Schema
```csharp
public class EuronextSymbolMapping
{
    public string Ticker { get; private set; }    // Primary key (e.g., AGAC)
    public string Isin { get; private set; }      // ISIN identifier (e.g., IE000FHBZDZ8)
    public string Mic { get; private set; }       // Market identifier (e.g., XAMS)
    public string? Name { get; private set; }     // Stock name
    public string Currency { get; private set; }  // Quote currency (e.g., USD, EUR)
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
```

### Database Table
```sql
CREATE TABLE euronext_symbol_mappings (
    ticker VARCHAR(20) PRIMARY KEY,
    isin VARCHAR(20) NOT NULL,
    mic VARCHAR(10) NOT NULL,
    name VARCHAR(255) NULL,
    currency VARCHAR(10) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
```

### Business Rules
- **Global cache**: Shared across all users
- **Auto-populated**: Created when Euronext quote is first fetched
- **TTL**: No expiration (symbols are stable)

---

## §11 Updated Relationships Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         User                                     │
│  (unchanged from 001-portfolio-tracker)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┬──────────────────┐
              │               │               │                  │
              │ 1:1           │ 1:N           │ 1:1              │
              ▼               ▼               ▼                  ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│   Portfolio     │  │  UserBenchmark  │  │ UserPreferences │  │  (other user    │
│   (unchanged)   │  │     [Phase 1]   │  │    [Phase 2]    │  │   entities)     │
└─────────────────┘  └─────────────────┘  └─────────────────┘  └─────────────────┘
        │
        │ 1:N
        ▼
┌─────────────────────────────────────────────────────────────────┐
│                   StockTransaction                               │
│  [Phase 1] + Market: StockMarket (enum)                         │
│  [Phase 2] + Currency: Currency (enum)                          │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                      StockSplit                                  │
│  [Phase 1] Standalone global table - not per-user               │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                  EuronextSymbolMapping                           │
│  [Phase 2] Global cache table - ticker to ISIN/MIC mapping      │
└─────────────────────────────────────────────────────────────────┘
```
