# Quickstart: Performance Refinements

**Date**: 2026-01-23
**Feature**: 004-performance-refinements

## Prerequisites

- .NET 8 SDK installed
- Node.js 20+ installed
- PostgreSQL running (or use SQLite for development)
- Docker (for production deployment)

---

## Development Setup

### 1. Backend

```bash
# Restore dependencies
dotnet restore backend/src/InvestmentTracker.API/InvestmentTracker.API.csproj

# Apply migrations (if any new ones added)
dotnet ef database update -p backend/src/InvestmentTracker.Infrastructure/InvestmentTracker.Infrastructure.csproj -s backend/src/InvestmentTracker.API/InvestmentTracker.API.csproj

# Run backend
dotnet run --project backend/src/InvestmentTracker.API/InvestmentTracker.API.csproj
```

Backend runs on `http://localhost:5000`

### 2. Frontend

```bash
cd frontend

# Install dependencies
npm install

# Run dev server
npm run dev
```

Frontend runs on `http://localhost:3000`

---

## Key Files to Modify

### Backend

| File | Change Type | Description |
|------|-------------|-------------|
| `Domain/Services/SimpleReturnCalculator.cs` | NEW | Simple return calculation logic |
| `Domain/Entities/MonthlyNetWorthSnapshot.cs` | NEW | Entity for monthly snapshots |
| `Domain/Entities/BenchmarkAnnualReturn.cs` | NEW | Entity for cached benchmark returns |
| `Application/DTOs/PerformanceDtos.cs` | MODIFY | Add Modified Dietz + TWR fields to YearPerformanceDto |
| `Application/Services/HistoricalPerformanceService.cs` | MODIFY | Use ReturnCalculator |
| `Application/Interfaces/IMonthlySnapshotService.cs` | NEW | Interface for snapshot service |
| `Infrastructure/Services/MonthlySnapshotService.cs` | NEW | Implementation |
| `Infrastructure/MarketData/YahooAnnualReturnService.cs` | NEW | Yahoo Total Return fetcher |
| `Infrastructure/Services/JwtTokenService.cs` | MODIFY | Change default expiration to 120 min |
| `API/Controllers/PerformanceController.cs` | MODIFY | Add monthly endpoint |
| `API/Controllers/MarketDataController.cs` | MODIFY | Add benchmark returns endpoint |

### Frontend

| File | Change Type | Description |
|------|-------------|-------------|
| `services/api.ts` | MODIFY | Add token refresh, new API methods |
| `components/performance/CurrencyToggle.tsx` | NEW | Toggle component |
| `components/dashboard/HistoricalValueChart.tsx` | MODIFY | Support monthly data |
| `pages/Performance.tsx` | MODIFY | Use Modified Dietz + TWR, add toggle |
| `pages/PositionDetail.tsx` | MODIFY | Display source currency P&L |
| `types/index.ts` | MODIFY | Add new type definitions |

---

## Testing

### Backend Tests

```bash
cd backend

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/InvestmentTracker.Domain.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

Key test files to create:
- `SimpleReturnCalculatorTests.cs` - Unit tests for calculation
- `MonthlySnapshotServiceTests.cs` - Integration tests for snapshot generation
- `YahooAnnualReturnServiceTests.cs` - Tests for Yahoo scraping

### Optimization Evidence (Group C Final Batch / C3)

- Yearly path (`HistoricalPerformanceServiceReturnTests.CalculateYearPerformanceAsync_SingleBuyOnYearStart_ModifiedDietzEqualsTwr`):
  - `txSnapshotService.BackfillSnapshotsAsync(...)` asserted `Times.Once`
  - `txSnapshotService.GetSnapshotsAsync(...)` asserted `Times.Once`
  - `historicalYearEndDataService.GetOrFetchYearEndPriceAsync(...)` asserted `Times.Never` when caller already provides year-end prices
  - `historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync(...)` asserted `Times.Never` when caller already provides year-end prices

- Yearly auto-fetch path (`HistoricalPerformanceServiceReturnTests.CalculateYearPerformanceAsync_AutoFetchSameTickerForYearStartAndYearEnd_DeduplicatesPriceAndFxCalls`):
  - `historicalYearEndDataService.GetOrFetchYearEndPriceAsync("VWRA", year, ...)` asserted `Times.Once`
  - `historicalYearEndDataService.GetOrFetchYearEndPriceAsync("VWRA", year - 1, ...)` asserted `Times.Once`
  - `historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year, ...)` asserted `Times.Once`
  - `historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year - 1, ...)` asserted `Times.Once`
  - Snapshot service calls (`BackfillSnapshotsAsync`/`GetSnapshotsAsync`) each asserted `Times.Once`

- Monthly path (`MonthlySnapshotServiceTests`):
  - `GetMonthlyNetWorthAsync_TaiwanTicker_UsesYahooPrice_WhenYahooHasData`: Yahoo historical price `Times.Once`, TWSE `Times.Never`
  - `GetMonthlyNetWorthAsync_TaiwanTicker_YahooMiss_FallsBackToTwse_AndSkipsFxCall`: Yahoo `Times.Once`, TWSE `Times.Once`
  - `GetMonthlyNetWorthAsync_SameTickerAcrossMonths_UsesYahooRangeSeriesCache`: same ticker over 3 months uses `GetHistoricalPriceSeriesAsync(...)` `Times.Once` and `GetHistoricalPriceAsync(...)` `Times.Never` (range cache hit across months)

- Transaction snapshot path (`TransactionPortfolioSnapshotServiceTests`):
  - `UpsertSnapshotAsync_YahooFirstForTaiwanTicker_AndCachesHistoricalLookupWithinRun`: Yahoo historical price `Times.Once`, TWSE `Times.Never`
  - `UpsertSnapshotAsync_TaiwanTicker_YahooMiss_FallsBackToTwse`: Yahoo `Times.Once`, TWSE `Times.Once`

Before/After proof points:
- Before optimization: TWSE/TPEx could be hit as primary source in historical Taiwan pricing paths.
- After optimization (C1+C2+C3): Yahoo is primary across yearly/monthly/transaction paths; TWSE/TPEx is fallback-only and call-count assertions verify the reduced dependency.

### Frontend Tests

```bash
cd frontend

# Run tests
npm test

# Run with coverage
npm run test -- --coverage
```

Key test files to create:
- `CurrencyToggle.test.tsx` - Toggle component tests
- `HistoricalValueChart.test.tsx` - Chart with monthly data tests

---

## Environment Variables

### Development (.env or appsettings.Development.json)

```json
{
  "Jwt": {
    "AccessTokenExpirationMinutes": 120,
    "RefreshTokenExpirationDays": 7
  }
}
```

### Production (Docker environment)

```yaml
environment:
  - Jwt__AccessTokenExpirationMinutes=120
  - Jwt__RefreshTokenExpirationDays=7
```

---

## Verification Checklist

### User Story 1: Annual Returns (Modified Dietz + TWR)
- [ ] Performance page shows Modified Dietz and TWR
- [ ] Both source and home currency returns displayed
- [ ] Formula correctly calculates for mid-year start

### User Story 2: Currency Toggle
- [ ] Toggle appears in performance comparison section
- [ ] Switching updates chart data
- [ ] Preference persists across sessions

### User Story 3: Source Currency P&L
- [ ] Position detail page shows source currency P&L
- [ ] Format: `+$1,200 USD (+15%)`
- [ ] Colors correct (green/red)

### User Story 4: Monthly Chart
- [ ] Chart shows monthly data points
- [ ] X-axis labels in YYYY-MM format
- [ ] All history from first transaction displayed

### User Story 5: Benchmark Total Return
- [ ] Historical year comparison shows Yahoo Total Return
- [ ] DataSource indicator visible
- [ ] Fallback works when Yahoo unavailable

### User Story 6: Login Session
- [ ] User stays logged in for 2+ hours of activity
- [ ] 401 triggers refresh attempt before logout
- [ ] Smooth experience (no unexpected logouts)

---

## Common Issues

### Yahoo API Rate Limiting
If seeing frequent failures from Yahoo:
- Check if IP is being rate limited
- Add delays between requests
- Cache aggressively

### Token Refresh Loop
If seeing continuous refresh attempts:
- Check refresh token validity
- Verify backend refresh endpoint working
- Clear localStorage and re-login

### Monthly Snapshot Performance
If monthly endpoint is slow:
- Check if price caching is working
- Consider batch price fetching
- Monitor database query performance

---

## Next Steps

After implementation:
1. Run `/speckit.tasks` to generate task breakdown
2. Implement in priority order (P1 → P3)
3. Create PR for each user story
4. Run full test suite before merge
