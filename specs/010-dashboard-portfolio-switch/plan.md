# Implementation Plan: Dashboard & Portfolio Switching Overhaul

**Branch**: `010-dashboard-portfolio-switch` | **Date**: 2026-02-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/010-dashboard-portfolio-switch/spec.md`

## Summary

Add a portfolio selector dropdown (with "All Portfolios" option) to Dashboard and Performance pages. When "All Portfolios" is selected, show aggregated investment metrics across all portfolios. Uses a hybrid aggregation strategy: frontend merges simple data (summaries, transactions, monthly net worth), while 3 new backend endpoints handle mathematically complex calculations (aggregate XIRR, aggregate annual performance with TWR/Modified Dietz, aggregate available years).

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x + React 18 (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, Vite, TanStack Query (frontend caching not yet used for these pages)
**Storage**: PostgreSQL (no schema changes — all aggregation is computed)
**Testing**: xUnit (backend), Vitest + React Testing Library (frontend)
**Target Platform**: Web (Docker self-hosted)
**Project Type**: Web application (frontend + backend)
**Performance Goals**: Aggregate Dashboard loads within same perceived latency as individual portfolio view (2-5 portfolios typical)
**Constraints**: <512MB RAM idle, single-user personal app, all portfolios TWD base currency
**Scale/Scope**: 2-5 portfolios per user, ~50-200 transactions per portfolio

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | PASS | New Use Cases in Application layer, Controllers in API layer, no domain layer changes |
| II. Multi-Tenancy | PASS | Aggregate endpoints filter by `currentUserService.UserId`, same as existing pattern |
| III. Accuracy First | PASS | XIRR and TWR calculated on backend using existing proven solvers. Frontend only sums/merges simple values |
| IV. Self-Hosted Friendly | PASS | No new external dependencies, no new containers, minimal resource impact |
| V. Technology Stack | PASS | C# .NET 8 backend, React/TypeScript frontend, PostgreSQL (no changes) |
| Database Standards | PASS | No new tables or migrations required |
| API Standards | PASS | New endpoints follow existing REST patterns and error conventions |
| Security Requirements | PASS | Aggregate endpoints validate user ownership via `currentUserService.UserId` |

**Post-Design Re-check**: All gates still pass. No new dependencies introduced beyond existing stack.

## Project Structure

### Documentation (this feature)

```text
specs/010-dashboard-portfolio-switch/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: architecture decisions
├── data-model.md        # Phase 1: entity/DTO definitions
├── quickstart.md        # Phase 1: development setup guide
├── contracts/           # Phase 1: API contract definitions
│   └── api-contracts.md
└── tasks.md             # Phase 2: task checklist (created by /speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Application/
│   │   ├── UseCases/
│   │   │   ├── Portfolio/
│   │   │   │   └── CalculateAggregateXirrUseCase.cs        # NEW
│   │   │   └── Performance/
│   │   │       ├── GetAggregateAvailableYearsUseCase.cs    # NEW
│   │   │       └── CalculateAggregateYearPerformanceUseCase.cs  # NEW
│   │   └── DTOs/
│   │       └── PerformanceDtos.cs                          # MODIFY (add aggregate DTOs if needed)
│   └── InvestmentTracker.API/
│       └── Controllers/
│           ├── PortfoliosController.cs                     # MODIFY (add aggregate/xirr action)
│           └── PerformanceController.cs                    # MODIFY (add aggregate routes)
└── tests/
    └── InvestmentTracker.Tests/
        └── UseCases/
            ├── CalculateAggregateXirrUseCaseTests.cs       # NEW
            └── CalculateAggregateYearPerformanceUseCaseTests.cs  # NEW

frontend/
├── src/
│   ├── contexts/
│   │   └── PortfolioContext.tsx                            # MODIFY (support "all" sentinel)
│   ├── components/
│   │   └── portfolio/
│   │       └── PortfolioSelector.tsx                       # MODIFY (add "All Portfolios" option)
│   ├── pages/
│   │   ├── Dashboard.tsx                                   # MODIFY (aggregate data flow)
│   │   ├── Performance.tsx                                 # MODIFY (aggregate data flow)
│   │   └── Portfolio.tsx                                   # MODIFY (auto-select on "all")
│   └── services/
│       └── api.ts                                          # MODIFY (add aggregate API functions)
└── tests/ (if applicable)
```

**Structure Decision**: Existing web application structure (backend + frontend). New backend code follows Clean Architecture — Use Cases in Application layer, Controller actions in API layer. No Infrastructure layer changes needed.

## Complexity Tracking

> No constitution violations. All changes follow existing patterns.
