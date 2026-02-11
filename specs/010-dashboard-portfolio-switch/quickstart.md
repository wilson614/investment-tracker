# Quickstart: Dashboard & Portfolio Switching Overhaul

**Feature**: 010-dashboard-portfolio-switch
**Branch**: `010-dashboard-portfolio-switch`

## Prerequisites

- .NET 8 SDK
- Node.js 18+
- PostgreSQL running (or Docker)
- Project builds: `dotnet build` (backend) and `npm run build` (frontend)

## Development Flow

### 1. Backend (New Aggregate Endpoints)

```bash
cd backend/src/InvestmentTracker.Application

# New Use Cases to create:
# - UseCases/Portfolio/CalculateAggregateXirrUseCase.cs
# - UseCases/Performance/GetAggregateAvailableYearsUseCase.cs
# - UseCases/Performance/CalculateAggregateYearPerformanceUseCase.cs

# New DTOs (or reuse existing):
# - DTOs/PerformanceDtos.cs (add AggregateYearPerformanceDto if needed)

cd ../InvestmentTracker.API

# New Controller actions:
# - Controllers/PortfoliosController.cs (add aggregate/xirr action)
# - Controllers/PerformanceController.cs (add aggregate/years and aggregate/year actions)
```

**Pattern to follow**: `GetTotalAssetsSummaryUseCase.cs` — iterates all user portfolios, aggregates results.

### 2. Frontend (Context + UI + Aggregation)

```bash
cd frontend/src

# Core changes:
# 1. contexts/PortfolioContext.tsx — support "all" sentinel value, change default
# 2. components/portfolio/PortfolioSelector.tsx — add "All Portfolios" option
# 3. pages/Dashboard.tsx — branch on isAllPortfolios, aggregate data fetching
# 4. pages/Performance.tsx — branch on isAllPortfolios, aggregate data fetching
# 5. services/api.ts — add aggregate API functions
# 6. pages/Portfolio.tsx — auto-select first portfolio when "all" is active
```

### 3. Testing

```bash
# Backend unit tests
cd tests
dotnet test

# Frontend type check
cd frontend
npm run type-check
```

## Key Architecture Decisions

1. **Sentinel value `"all"`** for portfolio selection — stored in localStorage, checked via `isAllPortfolios` derived boolean.
2. **Hybrid aggregation** — frontend merges simple data (summaries, transactions, monthly), backend handles complex calculations (XIRR, TWR, Modified Dietz).
3. **No database changes** — all aggregation is computed from existing data.
4. **3 new backend endpoints** — aggregate XIRR, aggregate year performance, aggregate available years.

## File Change Summary

| Layer | Files to Modify | Files to Create |
|-------|----------------|-----------------|
| Backend Application | - | 3 Use Cases |
| Backend API | PortfoliosController.cs, PerformanceController.cs | - |
| Frontend Context | PortfolioContext.tsx | - |
| Frontend Components | PortfolioSelector.tsx | - |
| Frontend Pages | Dashboard.tsx, Performance.tsx, Portfolio.tsx | - |
| Frontend Services | api.ts | - |
