# Implementation Plan: Portfolio Enhancements V2

**Branch**: `002-portfolio-enhancements` | **Date**: 2026-01-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-portfolio-enhancements/spec.md`
**Base Module**: Extends `001-portfolio-tracker`

## Summary

Enhance the existing investment portfolio tracker with 6 new features: optional exchange rate for transactions (enabling native currency cost tracking), dashboard pie chart visualization, Euronext exchange support, historical year performance, extended YTD support for all stock types, and bar chart performance visualization. All enhancements build upon the existing 001-portfolio-tracker infrastructure.

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts
**Storage**: PostgreSQL (primary), SQLite (development)
**Testing**: xUnit (backend), Jest/React Testing Library (frontend)
**Target Platform**: Docker containers, self-hosted NAS/VPS
**Project Type**: Web application (frontend + backend) - extending existing
**Performance Goals**: <3 second page loads, pie/bar charts render <2 seconds
**Constraints**: Backward compatible with existing data, no breaking changes to 001 features
**Scale/Scope**: Same as 001 (10 family members, 10,000 transactions per user)

## Constitution Check

*GATE: All principles verified ✓*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✓ Pass | New services follow existing layer separation |
| II. Multi-Tenancy | ✓ Pass | All new queries include user context filtering |
| III. Accuracy First | ✓ Pass | Nullable ExchangeRate uses decimal; separate cost tracking by currency |
| IV. Self-Hosted Friendly | ✓ Pass | Euronext API cached; no new external dependencies for core features |
| V. Technology Stack | ✓ Pass | Recharts already in use; no new frameworks |

## Project Structure

### Documentation (this feature)

```text
specs/002-portfolio-enhancements/
├── plan.md              # This file
├── research.md          # Phase 0 output - technical decisions
├── data-model.md        # Phase 1 output - entity changes
├── quickstart.md        # Phase 1 output - testing guide
└── tasks.md             # Phase 2 output - implementation tasks
```

### Source Code (extends existing structure)

```text
backend/
├── src/
│   ├── InvestmentTracker.API/
│   │   └── Controllers/
│   │       ├── MarketDataController.cs    # Extended: Euronext quotes
│   │       └── PerformanceController.cs   # Extended: Historical years
│   ├── InvestmentTracker.Domain/
│   │   └── Entities/
│   │       └── StockTransaction.cs        # Modified: Nullable ExchangeRate
│   ├── InvestmentTracker.Application/
│   │   └── Services/
│   │       ├── EuronextQuoteService.cs    # New
│   │       └── HistoricalPerformanceService.cs  # New
│   └── InvestmentTracker.Infrastructure/
│       └── External/
│           └── EuronextApiClient.cs       # New

frontend/
├── src/
│   ├── components/
│   │   ├── charts/
│   │   │   ├── AssetAllocationPieChart.tsx  # New
│   │   │   └── PerformanceBarChart.tsx      # New
│   │   └── forms/
│   │       └── StockTransactionForm.tsx     # Modified: Optional exchange rate
│   └── pages/
│       ├── Dashboard.tsx                    # Modified: Add pie chart
│       └── Performance.tsx                  # Modified: Year selector, bar charts
```

**Structure Decision**: Extend existing 001 structure. No new projects; add new services and components within existing layers.

## Key Design Decisions

| Topic | Decision | Reference |
|-------|----------|-----------|
| Nullable ExchangeRate | Make ExchangeRate nullable; separate cost tracking by currency | research.md §1 |
| Mixed Currency Costs | Display costs in source currency when no exchange rate; no forced TWD conversion | research.md §1 |
| Euronext API | Use ISIN-MIC format; cache with stale indicator on failure | research.md §2 |
| Historical Performance | Extend existing YTD logic to accept year parameter | research.md §3 |
| ETF Type Detection | Default to accumulating; Taiwan stocks only for dividend adjustment | research.md §4 |
| Pie Chart Library | Use existing Recharts PieChart component | research.md §5 |
| Bar Chart Library | Use existing Recharts BarChart component | research.md §5 |

## Entity Changes

| Entity | Change Type | Details |
|--------|-------------|---------|
| StockTransaction | Modified | ExchangeRate: decimal → decimal? (nullable) |
| EuronextQuoteCache | New | ISIN, MIC, Price, Currency, FetchedAt, IsStale |
| HistoricalPrice | Extended | Add Euronext market support |
| EtfClassification | New | Symbol, Market, Type (Accumulating/Distributing/Unknown) |

## New API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/market-data/euronext/{isin}` | GET | Fetch Euronext quote by ISIN |
| `/api/performance/year/{year}` | GET | Get performance metrics for specific year |
| `/api/performance/years` | GET | List available years with data |
| `/api/etf-classification/{symbol}` | GET/PUT | Get or set ETF type classification |

## Complexity Tracking

> No constitution violations requiring justification.

## Next Steps

1. Generate research.md with detailed technical decisions
2. Generate data-model.md with entity definitions
3. Generate quickstart.md with testing scenarios
4. Proceed to `/speckit.tasks` for implementation task breakdown
