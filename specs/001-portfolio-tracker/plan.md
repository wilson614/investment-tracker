# Implementation Plan: Family Investment Portfolio Tracker

**Branch**: `001-portfolio-tracker` | **Date**: 2026-01-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-portfolio-tracker/spec.md`

## Summary

Build a family investment portfolio tracker to replace manual spreadsheet systems. The system tracks stock transactions, currency holdings, and calculates performance metrics (XIRR, unrealized PnL) with multi-tenancy support. Key features include real-time stock quotes, historical returns, CAPE market context, and stock split handling for accurate historical data entry.

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query
**Storage**: SQLite (development), PostgreSQL (production-compatible)
**Testing**: xUnit (backend), Jest/React Testing Library (frontend)
**Target Platform**: Docker containers, self-hosted NAS/VPS
**Project Type**: Web application (frontend + backend)
**Performance Goals**: <3 second page loads, <512MB RAM idle
**Constraints**: Offline-capable after deployment, no external service dependencies for core features
**Scale/Scope**: 10 family members, 10,000 transactions per user

## Constitution Check

*GATE: All principles verified ✓*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✓ Pass | Domain/Infrastructure/API layers properly separated |
| II. Multi-Tenancy | ✓ Pass | EF Core global query filters for user isolation |
| III. Accuracy First | ✓ Pass | decimal types, XIRR tests, stock split adjustments |
| IV. Self-Hosted Friendly | ✓ Pass | Docker Compose, SQLite/PostgreSQL, env-driven config |
| V. Technology Stack | ✓ Pass | .NET 8, React, EF Core as specified |

## Project Structure

### Documentation (this feature)

```text
specs/001-portfolio-tracker/
├── plan.md              # This file
├── research.md          # Phase 0 output - technical decisions
├── data-model.md        # Phase 1 output - entity definitions
├── quickstart.md        # Phase 1 output - getting started guide
├── contracts/           # Phase 1 output - API contracts (if applicable)
└── tasks.md             # Phase 2 output - implementation tasks
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.API/           # Controllers, middleware, DTOs
│   ├── InvestmentTracker.Application/   # Use cases, DTOs
│   ├── InvestmentTracker.Domain/        # Entities, value objects, domain services
│   └── InvestmentTracker.Infrastructure/ # EF Core, external APIs, repositories
└── tests/
    ├── InvestmentTracker.Domain.Tests/
    └── InvestmentTracker.API.Tests/

frontend/
├── src/
│   ├── components/      # React components
│   ├── pages/           # Route pages
│   ├── services/        # API clients
│   ├── hooks/           # Custom React hooks
│   └── types/           # TypeScript types
└── tests/
```

**Structure Decision**: Web application with separate frontend and backend projects, following Clean Architecture in the backend.

## Key Design Decisions

| Topic | Decision | Reference |
|-------|----------|-----------|
| XIRR Algorithm | Newton-Raphson with Brent's fallback | research.md §1 |
| Cost Calculation | Moving average, recalculate from T=0 | research.md §2 |
| Multi-Tenancy | Row-level filtering with EF Core global filters | research.md §4 |
| Stock Split | Dedicated table, adjust at display time, preserve original data | research.md §18 |
| Quote Caching | Flicker-prevention only, always fetch on page load | research.md §14 |
| Market YTD | Backend service with benchmark ETF prices | research.md §16 |
| CAPE API | Backend proxy, 24hr cache, real-time adjustment for 11 markets | spec.md FR-030e |

## Entities

| Entity | Description | Details |
|--------|-------------|---------|
| User | Family member with auth | data-model.md §1 |
| Portfolio | User's investment portfolio (1 per user) | data-model.md §2 |
| StockTransaction | Buy/sell records | data-model.md §3 |
| CurrencyLedger | Foreign currency tracking | data-model.md §4 |
| CurrencyTransaction | Currency exchange/interest/spend | data-model.md §5 |
| RefreshToken | JWT refresh token storage | data-model.md §6 |
| HistoricalPrice | Year-end price cache | data-model.md §7 |
| **StockSplit** | **Stock split event records** | **data-model.md §8** |

## Recent Updates

### 2026-01-10: Stock Split Handling
- Added `StockSplit` entity to track split events (symbol, market, date, ratio)
- Adjustment logic: transactions before split date get adjusted shares/price at display time
- Original transaction data preserved unchanged
- Known split: 0050 (Taiwan) 1:4 split effective 2025-06-18
- Supports cumulative splits (multiple splits for same stock)

### 2026-01-09: Dashboard Improvements
- CAPE real-time adjustment expanded to 11 markets
- YTD benchmark comparison with dividend-adjusted returns
- Quote caching revised to flicker-prevention only

## Complexity Tracking

> No constitution violations requiring justification.

## Next Steps

See [tasks.md](./tasks.md) for implementation tasks (generated via `/speckit.tasks`).
