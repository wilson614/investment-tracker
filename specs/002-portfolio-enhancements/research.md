# Research: Portfolio Enhancements V2

**Date**: 2026-01-16
**Feature**: 002-portfolio-enhancements

## §1 Nullable Exchange Rate & Auto-Filled Historical FX

### Decision
Keep `StockTransaction.ExchangeRate` nullable for input, but **auto-fill missing exchange rates using historical FX rates on the transaction date** when computing TWD-based metrics/reports. Preserve original currency amounts.

### Rationale
- Reduces data entry friction while maintaining accurate TWD-based reporting
- Avoids multi-portfolio complexity for currency modes
- Keeps a single portfolio model while still enabling XIRR and cost basis in home currency
- Historical FX rates are immutable and suitable for caching

### Implementation
- Backend: Keep `ExchangeRate` as `decimal?` on `StockTransaction`
- Metrics: When ExchangeRate is null and home-currency conversion is required, lookup historical FX rate for `TransactionDate`
- Caching: Persist fetched historical FX rates (reuse existing Stooq exchange-rate path and cache table)
- UX: If historical FX lookup fails, prompt for manual exchange rate input for that transaction

### Alternatives Considered
1. **Force exchange rate input**: Rejected - creates bad data and friction
2. **Multiple portfolios for currency modes**: Rejected - user preference to avoid multi-portfolio support
3. **Separate per-currency metrics only**: Rejected - TWD-based reporting becomes incomplete

---

## §2 Euronext API Integration

### Decision
Use Euronext's public quote API at `https://live.euronext.com/en/ajax/getDetailedQuote/{ISIN}-{MIC}` with caching and stale fallback.

### Rationale
- Euronext provides free public API for real-time quotes
- ISIN-MIC format ensures unique identification (e.g., IE000FHBZDZ8-XAMS)
- Caching prevents rate limiting and reduces latency
- Stale indicator provides transparency when API fails

### Implementation
- New `EuronextApiClient` in Infrastructure layer
- Cache quotes for 5 minutes (configurable)
- On API failure: return cached quote with `IsStale = true`
- Frontend: Display stale indicator ("Price as of {timestamp}, may be outdated")

### MIC Codes for Common Euronext Markets
| Market | MIC |
|--------|-----|
| Amsterdam | XAMS |
| Paris | XPAR |
| Brussels | XBRU |
| Lisbon | XLIS |

### Historical Prices
- Euronext historical data requires research for year-end prices
- Fallback: Manual input via existing HistoricalPrice mechanism
- Future: Consider scraping or third-party data source

---

## §3 Historical Year Performance

### Decision
Extend existing YTD performance calculation to accept a year parameter, reusing the same XIRR and return calculation logic.

### Rationale
- YTD logic already handles date-range performance calculation
- Parameterizing the year is a minimal change
- Consistent calculation method across all time periods

### Implementation
- Add `year` parameter to existing performance endpoints
- Calculate using transactions from Jan 1 to Dec 31 of specified year
- Require year-end prices for all held positions (prompt if missing)
- Available years: derived from earliest transaction date to current year

### Year-End Price Requirements
- For each position held at year-end, need Dec 31 closing price
- Use existing `HistoricalPrice` table
- Prompt user to input missing prices (FR-030a)

---

## §4 ETF Type Classification & Dividend Adjustment

### Decision
Default unknown ETFs to accumulating type (no dividend adjustment). Only Taiwan stocks get dividend adjustment for YTD calculation.

### Rationale
- Most international ETFs held by users are accumulating (VWRA, VT, etc.)
- Taiwan stock dividends are well-documented and can be fetched
- Conservative approach: not adjusting is safer than wrong adjustment
- User can manually override classification if needed

### Implementation
- New `EtfClassification` entity: Symbol, Market, Type (enum)
- Type values: `Accumulating`, `Distributing`, `Unknown`
- Unknown defaults to Accumulating behavior
- Taiwan stocks: fetch dividend data from existing source
- Display "Unconfirmed type" badge for Unknown classifications

### Future Enhancements (Out of Scope)
- US stock dividend adjustment
- Fetch annual total return from reliable sources
- Auto-detect ETF type from fund data providers

---

## §5 Chart Visualization (Pie Chart & Bar Chart)

### Decision
Use existing Recharts library for both pie chart (asset allocation) and bar chart (performance comparison).

### Rationale
- Recharts already in frontend dependencies
- Consistent look and feel with any existing charts
- Good React integration and TypeScript support
- Responsive and accessible

### Implementation

#### Asset Allocation Pie Chart
```tsx
<PieChart>
  <Pie data={allocationData} dataKey="value" nameKey="category">
    {allocationData.map((entry, index) => (
      <Cell key={index} fill={COLORS[index % COLORS.length]} />
    ))}
  </Pie>
  <Tooltip formatter={(value) => formatCurrency(value)} />
  <Legend />
</PieChart>
```

#### Performance Bar Chart
```tsx
<BarChart data={performanceData}>
  <XAxis dataKey="name" />
  <YAxis tickFormatter={(v) => `${v}%`} />
  <Tooltip />
  <Bar dataKey="return" fill="#8884d8">
    <LabelList dataKey="return" position="top" formatter={(v) => `${v}%`} />
  </Bar>
</BarChart>
```

### Color Scheme
- Use consistent color palette across all charts
- Distinct colors for different asset categories
- Positive returns: green tones
- Negative returns: red tones

---

## §6 XIRR Handling for Mixed Currency Transactions

### Decision
For portfolios with mixed exchange rate presence, calculate XIRR in source currency when possible; exclude from TWD-based XIRR if exchange rate is null.

### Rationale
- XIRR requires consistent currency for cash flows
- Mixing currencies produces meaningless results
- Per-currency XIRR provides accurate returns in that currency

### Implementation
- TWD XIRR: Only include transactions with exchange rate
- USD XIRR: Include all USD transactions (with or without rate)
- Display both when applicable
- Clear labeling: "XIRR (TWD)", "XIRR (USD)"

---

## §7 Historical Year-End Price Cache

### Decision
Create a global `HistoricalYearEndData` table to cache year-end stock prices and exchange rates, using on-demand lazy loading. Cache is immutable once populated.

### Rationale
- **API Rate Limits**: Stooq and TWSE APIs have rate limits; caching prevents failures during performance calculations
- **Immutable Historical Data**: Year-end prices for completed years never change; safe to cache permanently
- **Global Scope**: Historical prices are the same for all users; no need for per-user caching
- **Performance**: Cached data enables faster performance page loads (target: <2 seconds with cached prices)

### Implementation

#### Cache Strategy
- **Lazy Loading**: On performance calculation, check cache first → fetch from API if missing → save to cache
- **No Current Year**: Never cache YTD data; current year prices are still changing
- **Immutable**: Once cached, data cannot be overwritten through application; errors require DB-level correction
- **Manual Entry**: Only allowed when API fetch fails AND cache entry doesn't exist

#### Data Structure
```csharp
public class HistoricalYearEndData
{
    public int Id { get; set; }
    public HistoricalDataType DataType { get; set; }  // StockPrice | ExchangeRate
    public string Ticker { get; set; }                // "VT", "0050", "USDTWD"
    public int Year { get; set; }                     // 2024
    public decimal Value { get; set; }                // Price or rate
    public string Currency { get; set; }              // "USD", "TWD"
    public DateTime ActualDate { get; set; }          // Actual trading date
    public string Source { get; set; }                // "Stooq", "TWSE", "Manual"
    public DateTime FetchedAt { get; set; }           // Cache timestamp
}
```

#### Cache Lookup Flow
```
1. Performance calculation needs VT year-end price for 2024
2. Check HistoricalYearEndData for (StockPrice, "VT", 2024)
3. If found → return cached value
4. If not found → fetch from Stooq API
   - On success → save to cache, return value
   - On failure → prompt user for manual entry
5. Manual entry → save to cache with Source="Manual"
```

#### Source Mapping
| Stock Type | API Source | Ticker Format |
|------------|------------|---------------|
| US/International | Stooq | Symbol (e.g., "VT", "VWRA.UK") |
| Taiwan | TWSE | Symbol (e.g., "0050") |
| Exchange Rate | Stooq | Currency pair (e.g., "USDTWD") |
| Euronext | Manual (initially) | ISIN (e.g., "IE000FHBZDZ8") |

### Alternatives Considered
1. **Per-user cache**: Rejected - same prices for everyone, wastes storage
2. **Allow cache overwrites**: Rejected - historical prices are immutable; overwrites suggest data integrity issues
3. **Cache all prices upfront**: Rejected - only cache on-demand to avoid unnecessary API calls
4. **Use existing HistoricalPrice table**: Considered but rejected - different purpose (HistoricalPrice is for benchmark data, HistoricalYearEndData is for user portfolio positions)
