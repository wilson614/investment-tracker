# Research: Performance Refinements

**Date**: 2026-01-23
**Feature**: 004-performance-refinements

## Overview

This document captures research findings for implementing performance refinements. All NEEDS CLARIFICATION items from Technical Context have been resolved.

---

## 1. Simple Return Calculation

### Decision
Use the Modified Dietz formula variant for Simple Return calculation.

### Formula
```
Simple Return = (End Value - Start Value - Net Contributions) / (Start Value + Net Contributions) × 100%
```

For first year (no start value):
```
Simple Return = (End Value - Net Contributions) / Net Contributions × 100%
```

### Rationale
- Clear specification in FR-002 and FR-003
- Aligns with industry standard for measuring period returns
- More intuitive than XIRR for single-year analysis
- Avoids annualization distortion of short-term returns

### Alternatives Considered
1. **XIRR (current)**: Rejected - confusing for single-year periods, annualization misleads
2. **Time-Weighted Return (TWR)**: Rejected - more complex, requires daily valuations
3. **Money-Weighted Return (MWR)**: Considered - Simple Return is essentially a simplified MWR

### Implementation Notes
- Create `SimpleReturnCalculator` in Domain layer
- All calculations use `decimal` type per Constitution III
- Handle edge cases: zero start value, negative contributions (withdrawals)

---

## 2. Yahoo Annual Total Return API

### Decision
Scrape Yahoo Finance Performance page for Annual Total Return data.

### Data Source
URL Pattern: `https://finance.yahoo.com/quote/{symbol}/performance`

The Performance page shows "Annual Total Return (%) History" table with year-by-year returns including dividends.

### Rationale
- Yahoo Finance provides Total Return (price + dividends) for ETFs
- More accurate than calculating from year-end prices alone
- Existing `YahooHistoricalPriceService` provides pattern for HTTP requests

### Alternatives Considered
1. **Calculate from year-end prices**: Rejected - doesn't include dividends
2. **Yahoo v8 chart API with dividends**: Complex to extract from events
3. **Third-party API (Alpha Vantage, IEX)**: Rejected - adds external dependencies

### Implementation Notes
- Create `YahooAnnualReturnService` in Infrastructure/MarketData
- Parse HTML or find underlying JSON endpoint
- Cache results in database (`BenchmarkAnnualReturn` table)
- Fallback to existing price-based calculation if scraping fails

### Risk Mitigation
- FR-018 requires fallback to existing calculation
- Log warnings when Yahoo data unavailable
- UI should indicate data source (Yahoo Total Return vs Calculated)

---

## 3. Monthly Net Worth Snapshots

### Decision
Calculate month-end net worth on-demand using historical prices from Yahoo/Stooq.

### Data Flow
1. User requests historical chart
2. Backend identifies all months from first transaction to present
3. For each month-end, calculate positions at that date
4. Fetch historical prices for each position (Yahoo primary, Stooq fallback)
5. Calculate total value in home currency
6. Cache results to avoid repeated API calls

### Rationale
- FR-014 specifies Yahoo primary, Stooq fallback
- FR-015 requires caching
- FR-016 requires all available history

### Data Model
```
MonthlyNetWorthSnapshot:
  - Id (Guid)
  - PortfolioId (Guid, FK)
  - Month (DateOnly) - first day of month for identification
  - TotalValueHome (decimal)
  - TotalContributions (decimal)
  - SnapshotData (JSON) - individual position values for debugging
  - CalculatedAt (DateTime)
  - CreatedAt/UpdatedAt (DateTime)
```

### Implementation Notes
- Lazy calculation: only compute when requested
- Incremental updates: recalculate only changed months
- Performance: batch price requests where possible

---

## 4. Token Refresh Mechanism

### Decision
Implement proactive token refresh in frontend API layer.

### Current State (Problem)
From `api.ts:75-84`:
```typescript
if (response.status === 401) {
  localStorage.removeItem('token');
  localStorage.removeItem('refreshToken');
  localStorage.removeItem('user');
  window.location.href = '/login';
  throw createApiError(401, 'Session expired...');
}
```
**Issue**: No attempt to use refreshToken before clearing session.

### Proposed Solution
1. On 401 response, check if refreshToken exists
2. Call `/api/auth/refresh` with refreshToken
3. If successful, update tokens and retry original request
4. If refresh fails, clear tokens and redirect to login

### Implementation Pattern
```typescript
async function fetchApi<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const response = await fetchWithToken(endpoint, options);

  if (response.status === 401) {
    const refreshed = await tryRefreshToken();
    if (refreshed) {
      // Retry with new token
      return fetchWithToken(endpoint, options).then(r => r.json());
    }
    // Refresh failed, redirect to login
    clearAuthAndRedirect();
  }
  // ... rest of handling
}
```

### Token Expiration Settings
Per clarification: Access Token = 120 minutes (FR-022)

Backend changes:
- `JwtTokenService.cs:35` default from 15 to 120 minutes
- Environment variable: `Jwt__AccessTokenExpirationMinutes=120`

---

## 5. Source Currency P&L Display

### Decision
Add source currency unrealized P&L alongside existing home currency display.

### Calculation
```
Source Currency P&L = (Current Price - Average Cost) × Total Shares
Source Currency P&L % = (Source P&L / Total Cost in Source Currency) × 100%
```

### Data Already Available
From `PositionDetail.tsx`, position already has:
- `averageCostPerShareSource` (source currency average cost)
- `totalShares`
- `currentPrice` (from quote in source currency)
- `totalCostSource` (total cost in source currency) - need to verify

### Implementation Notes
- Backend: Add `unrealizedPnlSource` and `unrealizedPnlSourcePercentage` to position DTO
- Frontend: Display in PositionDetail metrics section
- Format: `+$1,200 USD (+15%)` with appropriate color coding

---

## 6. Currency Toggle for Performance Comparison

### Decision
Add toggle component to switch between source/home currency display modes.

### State Management
- Store preference in `localStorage` key: `performance_currency_mode`
- Values: `'source'` | `'home'`
- Default: `'source'` (current behavior)

### UI Component
Simple toggle switch with labels:
- Left: "Source (USD)"
- Right: "Home (TWD)"

### Data Impact
- Source mode: Use existing `xirrPercentage` / `simpleReturnSource`
- Home mode: Use `xirrPercentageHome` / `simpleReturnHome`
- Benchmarks always displayed as-is (they're in their native currency)

---

## Summary of Decisions

| Topic | Decision | Key Points |
|-------|----------|------------|
| Simple Return | Modified Dietz variant | Clear formula from spec; Domain layer calculator |
| Yahoo Total Return | Scrape Performance page | Fallback to price-based; cache in DB |
| Monthly Snapshots | On-demand calculation | Yahoo primary, Stooq fallback; cache results |
| Token Refresh | Proactive refresh on 401 | Try refresh before logout; 120 min expiry |
| Source P&L | Add to position DTO | Calculate from existing data; display alongside home |
| Currency Toggle | localStorage preference | Toggle component; affects display mode only |
