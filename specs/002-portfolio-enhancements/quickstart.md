# Quickstart: Portfolio Enhancements V2

**Date**: 2026-01-14
**Feature**: 002-portfolio-enhancements

## Prerequisites

- 001-portfolio-tracker fully implemented and running
- PostgreSQL database with existing schema
- Node.js 18+ and .NET 8 SDK installed

## Quick Test Scenarios

### Scenario 1: Optional Exchange Rate

**Goal**: Verify transactions can be created without exchange rate

1. Navigate to Add Transaction page
2. Select a USD-denominated stock (e.g., VTI)
3. Enter: 10 shares at $200, leave Exchange Rate empty
4. Submit transaction
5. **Verify**: Transaction saved, cost displays as "USD 2,000.00"
6. **Verify**: Holdings page shows USD cost, not TWD

### Scenario 2: Mixed Currency Cost Display

**Goal**: Verify separate cost tracking for mixed transactions

1. Create Transaction A: VTI, 10 shares, $200, Exchange Rate = 31.5
2. Create Transaction B: VTI, 5 shares, $210, Exchange Rate = empty
3. Navigate to Holdings page
4. **Verify**: Two separate line items for VTI:
   - "VTI (TWD)": 10 shares, avg cost TWD 630/share
   - "VTI (USD)": 5 shares, avg cost USD 210/share

### Scenario 3: Dashboard Pie Chart

**Goal**: Verify asset allocation pie chart renders correctly

1. Ensure portfolio has multiple asset types (stocks, ETFs, currencies)
2. Navigate to Dashboard
3. **Verify**: Pie chart displays with distinct colors per category
4. **Verify**: Hover shows detailed amount and percentage
5. **Verify**: Legend shows all categories

### Scenario 4: Euronext Quote Fetch

**Goal**: Verify Euronext stock quotes work

1. Add a Euronext stock: AGAC (ISIN: IE000FHBZDZ8, Market: XAMS)
2. Navigate to Holdings page
3. Click "Fetch Quote" for AGAC
4. **Verify**: Price displays in USD
5. **Verify**: If API fails, shows cached price with "stale" indicator

### Scenario 5: Historical Year Performance

**Goal**: Verify past year performance calculation

1. Ensure transactions exist from 2024 or earlier
2. Ensure year-end prices exist for 2024
3. Navigate to Performance page
4. Select "2024" from year dropdown
5. **Verify**: XIRR and total return display for 2024
6. **Verify**: If prices missing, prompt appears to input them

### Scenario 6: ETF Type Classification

**Goal**: Verify ETF type marking and YTD behavior

1. Navigate to ETF Classifications (or Holdings detail)
2. Find an ETF marked as "Unknown"
3. **Verify**: Badge shows "Unconfirmed type"
4. Change classification to "Accumulating"
5. **Verify**: YTD calculation runs without dividend adjustment
6. For Taiwan stock: verify dividend adjustment applied

### Scenario 7: Performance Bar Chart

**Goal**: Verify bar chart visualization

1. Navigate to Performance Comparison page
2. **Verify**: Bar chart displays with multiple metrics
3. **Verify**: Each bar has distinct color and label
4. **Verify**: Hover shows detailed data
5. **Verify**: Positive returns in green, negative in red

## API Testing (curl examples)

### Fetch Euronext Quote
```bash
curl -X GET "http://localhost:5000/api/market-data/euronext/IE000FHBZDZ8" \
  -H "Authorization: Bearer $TOKEN"
```

### Get Historical Year Performance
```bash
curl -X GET "http://localhost:5000/api/performance/year/2024" \
  -H "Authorization: Bearer $TOKEN"
```

### List Available Years
```bash
curl -X GET "http://localhost:5000/api/performance/years" \
  -H "Authorization: Bearer $TOKEN"
```

### Get ETF Classification
```bash
curl -X GET "http://localhost:5000/api/etf-classification/VTI?market=US" \
  -H "Authorization: Bearer $TOKEN"
```

### Set ETF Classification
```bash
curl -X PUT "http://localhost:5000/api/etf-classification/VTI" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"market": "US", "type": "Accumulating"}'
```

## Database Verification

### Check Nullable ExchangeRate
```sql
SELECT id, symbol, shares, price_per_share, exchange_rate 
FROM stock_transactions 
WHERE exchange_rate IS NULL;
```

### Check Euronext Cache
```sql
SELECT * FROM euronext_quote_cache 
ORDER BY fetched_at DESC;
```

### Check ETF Classifications
```sql
SELECT * FROM etf_classification;
```

## Common Issues

| Issue | Solution |
|-------|----------|
| Pie chart not rendering | Check Recharts import; verify data format |
| Euronext API 403 | May need user-agent header; check rate limits |
| Missing year-end prices | Use HistoricalPrice manual input |
| XIRR returns null | Check if all transactions have dates; may need more data points |
