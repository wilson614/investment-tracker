# Data Model: Portfolio Enhancements V2

**Date**: 2026-01-14
**Feature**: 002-portfolio-enhancements

## Entity Changes Overview

This module extends the existing 001-portfolio-tracker data model with minimal changes to existing entities and a few new supporting entities.

---

## §1 StockTransaction (Modified)

**Change**: Make `ExchangeRate` nullable to support transactions without currency conversion.

### Current Schema
```csharp
public class StockTransaction
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public string Symbol { get; set; }
    public string Market { get; set; }
    public DateTime TransactionDate { get; set; }
    public TransactionType Type { get; set; }  // Buy, Sell
    public decimal Shares { get; set; }
    public decimal PricePerShare { get; set; }
    public string Currency { get; set; }
    public decimal ExchangeRate { get; set; }  // <-- CHANGE THIS
    public decimal Fees { get; set; }
    // ... audit fields
}
```

### Updated Schema
```csharp
public decimal? ExchangeRate { get; set; }  // Nullable - null means no TWD conversion
```

### Migration
```sql
ALTER TABLE stock_transactions 
ALTER COLUMN exchange_rate DROP NOT NULL;
```

### Business Rules
- When `ExchangeRate` is null:
  - Cost displays in `Currency` (e.g., "USD 1,234.56")
  - Transaction excluded from TWD-based XIRR
  - Average cost calculated separately from transactions with exchange rate
- When `ExchangeRate` is set:
  - Behavior unchanged from 001-portfolio-tracker

---

## §2 EuronextQuoteCache (New)

**Purpose**: Cache Euronext real-time quotes with stale indicator for API failure handling.

### Schema
```csharp
public class EuronextQuoteCache
{
    public string Isin { get; set; }          // Primary key part 1 (e.g., "IE000FHBZDZ8")
    public string Mic { get; set; }           // Primary key part 2 (e.g., "XAMS")
    public decimal Price { get; set; }
    public string Currency { get; set; }      // e.g., "USD", "EUR"
    public DateTime FetchedAt { get; set; }
    public DateTime? MarketTime { get; set; } // Quote timestamp from API
    public bool IsStale { get; set; }         // True if last fetch failed
}
```

### Indexes
- Primary Key: (Isin, Mic)

### Business Rules
- Cache TTL: 5 minutes (configurable)
- On successful fetch: Update all fields, `IsStale = false`
- On failed fetch: Keep existing data, set `IsStale = true`
- Frontend displays stale indicator when `IsStale = true`

---

## §3 EtfClassification (New)

**Purpose**: Track ETF type for dividend adjustment in YTD calculations.

### Schema
```csharp
public class EtfClassification
{
    public string Symbol { get; set; }        // Primary key part 1
    public string Market { get; set; }        // Primary key part 2
    public EtfType Type { get; set; }         // Accumulating, Distributing, Unknown
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; } // Null if system-determined
}

public enum EtfType
{
    Unknown = 0,
    Accumulating = 1,
    Distributing = 2
}
```

### Indexes
- Primary Key: (Symbol, Market)

### Business Rules
- Default: `Unknown` (treated as Accumulating for calculations)
- User can manually set classification
- System may auto-populate known ETFs in future

---

## §4 HistoricalPrice (Extended)

**Purpose**: Extend existing entity to support Euronext markets.

### Current Supported Markets
- TW (Taiwan)
- US (United States)

### New Supported Markets
- XAMS (Euronext Amsterdam)
- XPAR (Euronext Paris)
- XBRU (Euronext Brussels)
- XLIS (Euronext Lisbon)

### Schema (Unchanged)
```csharp
public class HistoricalPrice
{
    public string Symbol { get; set; }
    public string Market { get; set; }
    public int Year { get; set; }
    public decimal YearEndPrice { get; set; }
    public string Currency { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Business Rules
- For Euronext stocks, use ISIN as Symbol
- Market uses MIC code (XAMS, etc.)
- Manual input required until automated source identified

---

## §5 Relationships Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         User                                     │
│  (unchanged from 001-portfolio-tracker)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 1:1
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Portfolio                                  │
│  (unchanged from 001-portfolio-tracker)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 1:N
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   StockTransaction                               │
│  [MODIFIED] ExchangeRate: decimal → decimal? (nullable)         │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┴───────────────────┐
          │                                       │
          ▼                                       ▼
┌─────────────────────┐               ┌─────────────────────┐
│   HistoricalPrice   │               │  EtfClassification  │
│     [EXTENDED]      │               │       [NEW]         │
│  + Euronext markets │               │  Symbol, Market,    │
└─────────────────────┘               │  Type (enum)        │
                                      └─────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                   EuronextQuoteCache                             │
│  [NEW] Standalone cache table for Euronext API responses        │
└─────────────────────────────────────────────────────────────────┘
```

---

## §6 Migration Strategy

### Order of Migrations
1. Alter `stock_transactions.exchange_rate` to nullable
2. Create `euronext_quote_cache` table
3. Create `etf_classification` table
4. (No changes needed for `historical_price` - market values are flexible)

### Backward Compatibility
- Existing transactions with exchange rate: unchanged behavior
- Existing code reading ExchangeRate: handle null case
- No data migration required for existing records

### Rollback Plan
- Migration 1: Set default value for null exchange rates (e.g., 1.0) - data loss risk
- Migrations 2-3: Drop new tables (safe)
