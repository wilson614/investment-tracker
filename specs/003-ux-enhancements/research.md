# Research: UX Enhancements & Market Selection

**Date**: 2026-01-21
**Feature**: 003-ux-enhancements

## Research Summary

This module primarily extends existing functionality with minimal new technical challenges. Most patterns are already established in 001 and 002 modules.

---

## §1 Stock Split Calculation Logic

### Decision
Use multiplicative adjustment: Split ratio stored as decimal multiplier (e.g., 2.0 for 2:1 split means each share becomes 2 shares).

### Rationale
- Consistent with industry standard representation
- Simple calculation: `adjustedShares = originalShares * splitRatio`
- Handles both forward splits (ratio > 1) and reverse splits (ratio < 1)

### Alternatives Considered
1. **Fraction notation (2:1)**: Requires parsing, more error-prone
2. **Post-split count**: Ambiguous for partial holdings

### Implementation Notes
- Split adjustment is applied dynamically during portfolio calculation, not stored
- Original transaction shares remain unchanged in database
- Splits with dates in the future should be ignored until effective

---

## §2 Market Enum Design

### Decision
Extend existing `StockMarket` enum in Domain layer with explicit integer values:
```csharp
public enum StockMarket
{
    TW = 0,  // Taiwan Stock Exchange
    US = 1,  // US Markets (NYSE, NASDAQ)
    UK = 2,  // London Stock Exchange
    EU = 3   // Euronext (Amsterdam, Paris, etc.)
}
```

### Rationale
- Already exists in codebase (used in frontend)
- Integer values ensure stable database storage
- Matches existing `guessMarket()` logic in frontend

### Alternatives Considered
1. **String field**: More flexible but no type safety
2. **Separate Market table**: Over-engineering for fixed enum

---

## §3 Benchmark Real-time vs Historical Data Strategy

### Decision
- **Real-time quotes**: Fetched on-demand via Sina API (not cached in UserBenchmark)
- **Historical prices**: Use existing `HistoricalYearEndData` global cache (Stooq)
- **UserBenchmark table**: Only stores user's selection (ticker, market), not price data

### Rationale
- Avoids data duplication
- Leverages existing infrastructure from 002-portfolio-enhancements
- Real-time prices change frequently, caching adds complexity

### Alternatives Considered
1. **Cache real-time in UserBenchmark**: Stale data risk, storage overhead
2. **Separate BenchmarkPriceCache**: Duplicate of existing HistoricalYearEndData

---

## §4 Taiwan Timezone Implementation

### Decision
- Backend continues to store all dates in UTC
- Frontend applies UTC+8 offset for display only
- Use `date-fns-tz` or native `Intl.DateTimeFormat` with `Asia/Taipei` timezone

### Rationale
- Follows Constitution principle (backend stores UTC)
- No database migration required
- Localization is presentation concern

### Implementation
```typescript
// utils/dateUtils.ts
export const formatToTaiwanTime = (date: Date | string): string => {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleString('zh-TW', { timeZone: 'Asia/Taipei' });
};
```

---

## §5 Date Input Auto-Tab Pattern

### Decision
Use `onInput` event handler with character count check:
```typescript
const handleYearInput = (e: React.FormEvent<HTMLInputElement>) => {
  const value = e.currentTarget.value;
  if (value.length === 4 && /^\d{4}$/.test(value)) {
    monthInputRef.current?.focus();
  }
};
```

### Rationale
- Simple, no external library needed
- Works with controlled and uncontrolled inputs
- Non-intrusive (doesn't prevent typing)

### Alternatives Considered
1. **react-input-mask**: Heavy dependency for simple feature
2. **Form library auto-advance**: Not available in current setup

---

## §6 Existing Year-End Data for Dashboard Chart

### Decision
Reuse existing `YearEndData` or Performance calculation that already computes year-end portfolio values.

### Research Finding
From codebase search:
- `HistoricalPerformanceService` already calculates year-end values
- `GET /api/performance/historical` returns yearly data
- No new API endpoint needed; frontend chart consumes existing data

### Implementation
```typescript
// Dashboard.tsx
const { data: historicalData } = useQuery({
  queryKey: ['performance', 'historical', portfolioId],
  queryFn: () => performanceApi.getHistorical(portfolioId),
});

// Render Recharts LineChart with historicalData.yearlySnapshots
```

---

## §7 Migration Strategy for Market Field

### Decision
1. Add nullable `Market` column to `stock_transactions`
2. Run data migration to populate based on ticker format (guessMarket logic)
3. Set column as NOT NULL with default after migration

### Migration SQL
```sql
-- Step 1: Add nullable column
ALTER TABLE stock_transactions ADD COLUMN market INTEGER NULL;

-- Step 2: Populate based on ticker pattern
UPDATE stock_transactions SET market =
  CASE
    WHEN ticker ~ '^[0-9]+[A-Za-z]*$' THEN 0  -- TW
    WHEN ticker LIKE '%.L' THEN 2              -- UK
    ELSE 1                                      -- US (default)
  END
WHERE market IS NULL;

-- Step 3: Set NOT NULL
ALTER TABLE stock_transactions ALTER COLUMN market SET NOT NULL;
ALTER TABLE stock_transactions ALTER COLUMN market SET DEFAULT 1;
```

---

## §8 Non-Accumulating ETF Detection

### Decision
Use existing `EtfClassification` table from 002 module to determine if benchmark should show dividend warning.

### Implementation
- When user adds benchmark, check `EtfClassification` for `Type = Distributing`
- If distributing, show info message in UI
- No additional API needed

---

## Unresolved Items

None - all technical decisions resolved.
