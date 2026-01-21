# Implementation Plan: UX Enhancements & Market Selection

**Branch**: `003-ux-enhancements` | **Date**: 2026-01-21 | **Spec**: `specs/003-ux-enhancements/spec.md`
**Input**: Feature specification from `specs/003-ux-enhancements/spec.md`
**Base Module**: Extends `001-portfolio-tracker` and `002-portfolio-enhancements`

## Summary

This feature module delivers 8 UX improvements and data model enhancements:

1. **Transaction Market Selection (P1)**: Add Market field to StockTransaction table, allowing users to select market during transaction creation instead of relying on localStorage cache
2. **Benchmark Custom Stocks (P1)**: User-specific benchmark selection with Taiwan/international stock support via Sina/Stooq APIs
3. **Dashboard Historical Chart (P2)**: Line chart showing year-end portfolio value trends
4. **Stock Split Settings UI (P2)**: CRUD interface in Settings page for shared stock split data
5. **Default Dashboard Landing (P3)**: Redirect to Dashboard after login
6. **Taiwan Timezone Display (P3)**: Convert all time displays to UTC+8
7. **Date Input Auto-Tab (P3)**: Auto-focus to next field after 4-digit year input
8. **Fee Default Empty (P3)**: Remove auto-fill 0 for fee field

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
