# Quickstart: UX Enhancements & Market Selection

**Date**: 2026-01-21
**Feature**: 003-ux-enhancements

## Prerequisites

- Existing 001-portfolio-tracker and 002-portfolio-enhancements modules deployed
- .NET 8 SDK
- Node.js 18+
- PostgreSQL 14+

## Setup Steps

### 1. Backend - Run Migrations

```bash
cd backend/src/InvestmentTracker.Infrastructure

# Add migrations (if not already created)
dotnet ef migrations add AddMarketToTransaction -s ../InvestmentTracker.API
dotnet ef migrations add CreateStockSplitTable -s ../InvestmentTracker.API
dotnet ef migrations add CreateUserBenchmarkTable -s ../InvestmentTracker.API

# Apply migrations
dotnet ef database update -s ../InvestmentTracker.API
```

### 2. Backend - Build and Run

```bash
cd backend
dotnet build
dotnet run --project src/InvestmentTracker.API
```

### 3. Frontend - Install and Run

```bash
cd frontend
npm install
npm run dev
```

## Key Files to Modify

### Backend

| File | Change |
|------|--------|
| `Domain/Entities/StockTransaction.cs` | Add `Market` property |
| `Domain/Entities/StockSplit.cs` | New file |
| `Domain/Entities/UserBenchmark.cs` | New file |
| `Domain/Enums/StockMarket.cs` | New enum (or extend existing) |
| `Application/UseCases/StockSplit/` | New CRUD use cases |
| `Application/UseCases/Benchmark/` | New use cases |
| `API/Controllers/StockSplitController.cs` | New controller |
| `API/Controllers/TransactionController.cs` | Add market handling |
| `Infrastructure/Persistence/AppDbContext.cs` | Add DbSets |

### Frontend

| File | Change |
|------|--------|
| `components/transactions/TransactionForm.tsx` | Add market dropdown, fix fee default |
| `components/settings/StockSplitSettings.tsx` | New component |
| `components/dashboard/HistoricalValueChart.tsx` | New component |
| `pages/Settings.tsx` | Add stock split section |
| `pages/Dashboard.tsx` | Add historical chart |
| `App.tsx` | Change default route to /dashboard |
| `utils/dateUtils.ts` | Add Taiwan timezone helpers |

## Testing Verification

### Unit Tests
```bash
cd backend
dotnet test
```

### Manual Testing Checklist

1. **Transaction Market Selection**
   - [ ] Create transaction with auto-predicted market
   - [ ] Manually change market and save
   - [ ] Refresh page, verify market persists
   - [ ] Check position card shows correct market

2. **Stock Split Settings**
   - [ ] Navigate to Settings > Stock Splits
   - [ ] Add new split record
   - [ ] Edit existing split
   - [ ] Delete split
   - [ ] Verify position share count adjusts

3. **User Benchmark**
   - [ ] Add custom benchmark stock
   - [ ] View on Performance page
   - [ ] Verify dividend warning for distributing ETFs
   - [ ] Delete benchmark

4. **Dashboard Chart**
   - [ ] View historical value line chart
   - [ ] Hover to see data points
   - [ ] Verify data matches year-end values

5. **UX Improvements**
   - [ ] Login redirects to Dashboard
   - [ ] Dates display in Taiwan timezone
   - [ ] Year input auto-tabs to month
   - [ ] Fee field starts empty

## Common Issues

### Migration Fails
```bash
# Reset and re-apply
dotnet ef database drop -s ../InvestmentTracker.API
dotnet ef database update -s ../InvestmentTracker.API
```

### Market Value Incorrect
- Check `guessMarket()` logic in migration matches frontend logic
- Verify enum values: TW=0, US=1, UK=2, EU=3

### Stock Split Not Applying
- Ensure split date is before transaction date
- Check split ratio is > 0
- Verify ticker matches exactly (case-sensitive)
