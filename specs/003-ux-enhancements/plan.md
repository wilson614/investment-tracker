# Implementation Plan: UX Enhancements & Market Selection

**Branch**: `003-ux-enhancements` | **Date**: 2026-01-21 | **Spec**: `specs/003-ux-enhancements/spec.md`
**Input**: Feature specification from `specs/003-ux-enhancements/spec.md`
**Base Module**: Extends `001-portfolio-tracker` and `002-portfolio-enhancements`

## Summary

This feature module delivers UX improvements and data model enhancements in two phases:

### Phase 1 (US1-US8) - COMPLETED ✅

1. **Transaction Market Selection (P1)**: Add Market field to StockTransaction table, allowing users to select market during transaction creation instead of relying on localStorage cache
2. **Benchmark Custom Stocks (P1)**: User-specific benchmark selection with Taiwan/international stock support via Sina/Stooq APIs
3. **Dashboard Historical Chart (P2)**: Line chart showing year-end portfolio value trends
4. **Stock Split Settings UI (P2)**: CRUD interface in Settings page for shared stock split data
5. **Default Dashboard Landing (P3)**: Redirect to Dashboard after login
6. **Taiwan Timezone Display (P3)**: Convert all time displays to UTC+8
7. **Date Input Auto-Tab (P3)**: Auto-focus to next field after 4-digit year input
8. **Fee Default Empty (P3)**: Remove auto-fill 0 for fee field

### Phase 2 (US9-US17) - COMPLETED ✅

9. **Transaction Currency Field (P1)**: Add Currency field to transactions with auto-detection (TW→TWD, others→USD)
10. **XIRR Current Year Warning (P2)**: Display warning for short-period XIRR calculations
11. **Logout Cache Cleanup (P1)**: Clear user-specific caches on logout, store preferences in DB
12. **Dashboard Layout Stability (P2)**: Prevent layout shift during loading
13. **Multi-Market Same-Ticker (P1)**: Group positions by (ticker, market) composite key
14. **Quote Fetching Enforcement (P1)**: No market fallback for position cards
15. **Ticker Prediction Trigger (P3)**: Trigger on 4th character instead of blur
16. **CSV Import Market/Currency (P2)**: Add required Market and Currency columns
17. **Yahoo Historical Price Fallback (P2)**: Yahoo as primary, Stooq as fallback

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts
**Storage**: PostgreSQL (primary), SQLite (development fallback)
**Testing**: xUnit (backend), Jest/React Testing Library (frontend)
**Target Platform**: Docker containers, self-hosted NAS/VPS
**Project Type**: Web application (frontend + backend)
**Performance Goals**: Dashboard chart load < 3 seconds, position recalculation < 2 seconds
**Constraints**: Self-hosted friendly, < 512MB RAM idle

## Constitution Check

*GATE: All principles verified ✓*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✓ Pass | New entities in Domain, services in Application, APIs in Controllers |
| II. Multi-Tenancy | ✓ Pass | UserBenchmark is per-user; StockSplit is global shared data |
| III. Accuracy First | ✓ Pass | Stock split calculations affect share count; decimal precision maintained |
| IV. Self-Hosted Friendly | ✓ Pass | No new external service dependencies beyond existing Sina/Stooq |
| V. Technology Stack | ✓ Pass | Uses existing stack (C#/.NET 8, React, PostgreSQL) |

## Project Structure

### Documentation (this feature)

```text
specs/003-ux-enhancements/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (API contracts)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   │   ├── StockSplit.cs           # NEW
│   │   │   └── UserBenchmark.cs        # NEW
│   │   └── Services/
│   │       └── StockSplitAdjustmentService.cs  # NEW
│   ├── InvestmentTracker.Application/
│   │   └── UseCases/
│   │       ├── StockSplit/             # NEW
│   │       └── Benchmark/              # NEW
│   ├── InvestmentTracker.Infrastructure/
│   │   └── Persistence/
│   │       └── Migrations/             # New migrations
│   └── InvestmentTracker.API/
│       └── Controllers/
│           ├── StockSplitController.cs # NEW
│           └── BenchmarkController.cs  # MODIFIED
└── tests/

frontend/
├── src/
│   ├── components/
│   │   ├── settings/
│   │   │   └── StockSplitSettings.tsx  # NEW
│   │   ├── dashboard/
│   │   │   └── HistoricalValueChart.tsx # NEW
│   │   └── transactions/
│   │       └── TransactionForm.tsx      # MODIFIED (market dropdown, fee default)
│   ├── pages/
│   │   └── Settings.tsx                 # MODIFIED (add stock split section)
│   └── utils/
│       └── dateUtils.ts                 # MODIFIED (timezone helpers)
└── tests/
```

## Key Design Decisions

### 1. Transaction Market Field (P1)

- **Change**: Add `Market` field to `StockTransaction` entity (enum: TW=0, US=1, UK=2, EU=3)
- **Migration**: Auto-populate existing transactions using `guessMarket()` logic
- **Frontend**: Add market dropdown to TransactionForm with auto-predict as default
- **Quote Fetching**: Read market from latest transaction instead of guessing

### 2. UserBenchmark Entity (P1)

- **Scope**: Per-user storage for personalized benchmark selections
- **Fields**: UserId, Ticker, Market, DisplayName, AddedAt
- **API**: CRUD endpoints under `/api/user-benchmarks`
- **Historical Data**: Uses existing `HistoricalYearEndData` (shared global cache)

### 3. StockSplit Entity (P2)

- **Scope**: Global shared data (all users can CRUD)
- **Fields**: Ticker, SplitDate, SplitRatio (decimal, e.g., 2.0 for 2:1 split)
- **Recalculation**: On split record change, trigger position share count adjustment
- **UI Location**: Settings page sub-section

### 4. Dashboard Historical Chart (P2)

- **Data Source**: Existing `YearEndData` table (already has year-end portfolio values)
- **Chart Library**: Recharts (already in use)
- **Granularity**: Year-end snapshots only (as clarified in spec)

### 5. Frontend UX Improvements (P3)

- **Default Route**: Change root redirect from `/portfolio` to `/dashboard`
- **Timezone**: Add `formatToTaiwanTime()` utility, apply to all date displays
- **Date Input**: Add `onInput` handler for year field auto-tab after 4 digits
- **Fee Field**: Remove `defaultValue={0}` from fee input

## Notes

- Stock split calculations are critical for accuracy; extensive unit tests required
- Market selection migration is one-time; reversible by setting all to null
- UserBenchmark integrates with existing benchmark comparison UI on Performance page

---

## Phase 2 Key Design Decisions (US9-US17)

### 6. Transaction Currency Field (US9)

- **Change**: Add `Currency` field to `StockTransaction` entity (enum: TWD=0, USD=1, GBP=2, EUR=3)
- **Auto-Detection**: Taiwan stocks (market=TW) → TWD, all others → USD
- **User Override**: Currency dropdown in TransactionForm, user can manually select
- **Migration**: Auto-populate existing transactions based on Market field

### 7. XIRR Current Year Warning (US10)

- **Condition**: Display warning when calculation period < 3 months
- **UI**: Yellow warning icon with tooltip explaining short-term volatility
- **Location**: Dashboard XIRR display and Performance page

### 8. Logout Cache Cleanup (US11)

- **Strategy**: Use `user_` prefix for user-specific localStorage keys
- **Logout Action**: Clear all `user_*` keys + invalidate React Query cache
- **DB Storage**: Move `ytd_prefs` and `cape_region_prefs` to User table or UserPreferences table
- **Remove**: `selected_portfolio_id`/`default_portfolio_id` (single portfolio per user, fetch from API)

### 9. Dashboard Layout Stability (US12)

- **Historical Chart**: Always render container with min-height, show "No data" placeholder
- **CAPE Section**: Fixed minimum height during loading state
- **Implementation**: Use skeleton loaders with matching dimensions

### 10. Multi-Market Same-Ticker Support (US13)

- **Position Grouping**: Change from `GROUP BY ticker` to `GROUP BY (ticker, market)`
- **Backend**: Update `GetPortfolioSummaryUseCase` to include market in grouping
- **Frontend**: Display market badge on each position card

### 11. Quote Fetching Market Enforcement (US14)

- **Position Cards**: Strictly use position's market, NO fallback
- **Ticker Prediction Only**: Allow US→UK fallback during TransactionForm auto-detection
- **Failure Display**: Show "無報價" or last cached value

### 12. Ticker Prediction Trigger (US15)

- **Trigger**: Fire detection on 4th character via `onInput` handler
- **Debounce**: Cancel previous request when new input arrives
- **Loading State**: Show spinner in market dropdown during detection

### 13. CSV Import Market/Currency (US16)

- **Required Columns**: Add `Market` and `Currency` to CSV schema
- **Validation**: Fail import if columns missing
- **Template Update**: Include Market/Currency columns with example values

### 14. Yahoo Historical Price Fallback (US17)

- **Primary Source**: Yahoo Finance (already implemented in `YahooHistoricalPriceService`)
- **Fallback Source**: Stooq (existing `StooqHistoricalPriceService`)
- **Integration Point**: Update `HistoricalYearEndDataService` to try Yahoo first, then Stooq

## Phase 2 Project Structure Additions

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   │   └── StockTransaction.cs      # ADD: Currency field
│   │   └── Enums/
│   │       └── Currency.cs              # NEW: Currency enum
│   ├── InvestmentTracker.Application/
│   │   └── UseCases/
│   │       └── Portfolio/
│   │           └── GetPortfolioSummaryUseCase.cs  # MODIFY: (ticker, market) grouping
│   └── InvestmentTracker.Infrastructure/
│       └── Services/
│           └── HistoricalYearEndDataService.cs    # MODIFY: Yahoo-first fallback

frontend/
├── src/
│   ├── components/
│   │   ├── transactions/
│   │   │   └── TransactionForm.tsx      # MODIFY: Currency dropdown, 4-char trigger
│   │   └── dashboard/
│   │       └── *.tsx                    # MODIFY: Layout stability
│   ├── hooks/
│   │   └── useAuth.tsx                  # MODIFY: Cache cleanup on logout
│   └── contexts/
│       └── PortfolioContext.tsx         # MODIFY: Remove localStorage portfolio ID
```
