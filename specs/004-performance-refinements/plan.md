# Implementation Plan: Performance Refinements

**Branch**: `004-performance-refinements` | **Date**: 2026-01-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/004-performance-refinements/spec.md`

## Summary

This feature optimizes performance analysis by:
1. Replacing annual XIRR with Simple Return for more intuitive yearly metrics
2. Adding source/home currency toggle for performance comparison
3. Displaying source currency unrealized P&L on position details
4. Upgrading historical net worth chart from yearly to monthly data points
5. Fetching Yahoo Annual Total Return for benchmark comparison
6. Extending Access Token expiration to 120 minutes and verifying refresh mechanism

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x with React 19 (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core 8, React 19, Vite, TanStack Query, Recharts
**Storage**: PostgreSQL (production), SQLite (development fallback)
**Testing**: xUnit (backend), Vitest + React Testing Library (frontend)
**Target Platform**: Docker containers (Linux), Web browsers
**Project Type**: Web application (frontend + backend)
**Performance Goals**: Chart renders smoothly with 60+ months of data
**Constraints**: <500ms API response for monthly data, reuse existing Yahoo/Stooq/Sina integrations
**Scale/Scope**: Single-user self-hosted, ~50 holdings max, 5+ years of history

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✅ PASS | Changes span Domain (calculation), Application (services), Infrastructure (external APIs), API (endpoints), Frontend (components) - proper layer separation |
| II. Multi-Tenancy | ✅ PASS | All queries already include user context filtering; no changes to isolation model |
| III. Accuracy First | ✅ PASS | Simple Return formula clearly defined; all monetary calculations use `decimal`; unit tests required |
| IV. Self-Hosted Friendly | ✅ PASS | No new external dependencies; uses existing Yahoo/Stooq/Sina APIs with fallback |
| V. Technology Stack | ✅ PASS | C#/.NET 8, React/TypeScript, PostgreSQL, EF Core - all within approved stack |

**Database Standards**: ✅ Monthly snapshots will include timestamps; cache tables have proper structure
**API Standards**: ✅ Consistent error responses; JWT with configurable expiration (FR-022, FR-023)
**Security Requirements**: ✅ Argon2 already in use; JWT refresh mechanism to be verified

## Project Structure

### Documentation (this feature)

```text
specs/004-performance-refinements/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-changes.md   # API endpoint changes
├── checklists/          # Quality checklists
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   │   └── MonthlyNetWorthSnapshot.cs     # NEW: Monthly snapshot entity
│   │   └── Services/
│   │       └── SimpleReturnCalculator.cs       # NEW: Simple return calculation
│   ├── InvestmentTracker.Application/
│   │   ├── DTOs/
│   │   │   └── PerformanceDtos.cs              # MODIFY: Add SimpleReturn fields
│   │   ├── Interfaces/
│   │   │   └── IMonthlySnapshotService.cs      # NEW: Monthly snapshot interface
│   │   └── Services/
│   │       └── HistoricalPerformanceService.cs # MODIFY: Use SimpleReturn
│   ├── InvestmentTracker.Infrastructure/
│   │   ├── MarketData/
│   │   │   └── YahooAnnualReturnService.cs     # NEW: Yahoo Total Return fetcher
│   │   └── Services/
│   │       └── MonthlySnapshotService.cs       # NEW: Monthly snapshot impl
│   └── InvestmentTracker.API/
│       └── Controllers/
│           └── PerformanceController.cs        # MODIFY: Add monthly endpoint
└── tests/
    ├── InvestmentTracker.Domain.Tests/
    │   └── SimpleReturnCalculatorTests.cs      # NEW: Unit tests
    └── InvestmentTracker.Application.Tests/
        └── MonthlySnapshotServiceTests.cs      # NEW: Integration tests

frontend/
├── src/
│   ├── components/
│   │   ├── dashboard/
│   │   │   └── HistoricalValueChart.tsx        # MODIFY: Monthly data support
│   │   ├── performance/
│   │   │   ├── CurrencyToggle.tsx              # NEW: Currency mode toggle
│   │   │   └── YearPerformanceCard.tsx         # MODIFY: SimpleReturn display
│   │   └── portfolio/
│   │       └── PositionDetail.tsx            # MODIFY: Source currency P&L display
│   ├── pages/
│   │   ├── Performance.tsx                     # MODIFY: Toggle + SimpleReturn
│   │   └── PositionDetail.tsx                  # MODIFY: Source currency P&L
│   ├── services/
│   │   └── api.ts                              # MODIFY: New endpoints
│   └── hooks/
│       └── useAuth.tsx                         # VERIFY: Refresh mechanism
└── tests/
    └── components/
        └── CurrencyToggle.test.tsx             # NEW: Toggle tests
```

**Structure Decision**: Web application with existing backend/frontend separation. All changes follow established patterns in each layer.

## Complexity Tracking

> No constitution violations. All changes fit within existing architecture.

| Area | Complexity | Mitigation |
|------|------------|------------|
| Monthly data volume | Medium | Cache month snapshots; fetch full daily series per ticker and derive month-end points server-side; invalidate from changed month on transaction edits |
| Yahoo Annual Return scraping | Medium | Fallback to existing calculation if unavailable |
| Token refresh verification | Low | Existing mechanism, just verify + extend timeout |
