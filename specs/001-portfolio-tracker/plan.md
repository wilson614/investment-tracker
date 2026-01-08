# Implementation Plan: Family Investment Portfolio Tracker

**Branch**: `001-portfolio-tracker` | **Date**: 2026-01-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-portfolio-tracker/spec.md`
**Status**: Updated - Dashboard Analytics phase added

## Summary

Build a family investment portfolio tracker to replace manual spreadsheet tracking. The system supports stock/ETF transactions, currency ledger management, real-time quotes, and multi-tenancy for family members.

**New in this update**: Dashboard analytics with historical returns, CAPE market context, and historical price API integration.

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**:
- Backend: ASP.NET Core, Entity Framework Core, FluentValidation
- Frontend: React 18, Vite, TailwindCSS, React Router

**Storage**: SQLite (development), PostgreSQL (production-compatible)
**Testing**: xUnit (backend), Jest (frontend)
**Target Platform**: Docker containers (self-hosted), Web browser
**Project Type**: Web application (backend + frontend)
**Performance Goals**: <3s page load, <5s metric calculation
**Constraints**: <512MB RAM idle, offline-capable core features
**Scale/Scope**: 10 family members, 10,000 transactions per user

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✅ PASS | Domain/Application/Infrastructure/API layers implemented |
| II. Multi-Tenancy | ✅ PASS | Row-level filtering with EF Core global query filters |
| III. Accuracy First | ✅ PASS | decimal types, XIRR with Newton-Raphson, unit tests |
| IV. Self-Hosted Friendly | ✅ PASS | Docker-ready, env-var config, SQLite for dev |
| V. Technology Stack | ✅ PASS | C# .NET 8, React, Entity Framework Core |

**Technical Constraints Compliance**:
- ✅ All tables have `created_at` and `updated_at` timestamps
- ✅ Soft deletes for financial records (IsDeleted/IsActive flags)
- ✅ Foreign keys indexed
- ✅ JWT authentication with configurable expiration
- ✅ Input validation at API boundary (FluentValidation)

## Project Structure

### Documentation (this feature)

```text
specs/001-portfolio-tracker/
├── plan.md              # This file
├── research.md          # Phase 0 output (updated for dashboard)
├── data-model.md        # Phase 1 output (updated for dashboard)
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Services/
│   │   └── ValueObjects/
│   ├── InvestmentTracker.Application/
│   │   ├── DTOs/
│   │   ├── UseCases/
│   │   ├── Validators/
│   │   └── Interfaces/
│   ├── InvestmentTracker.Infrastructure/
│   │   ├── Persistence/
│   │   ├── Repositories/
│   │   └── Services/
│   └── InvestmentTracker.API/
│       ├── Controllers/
│       └── Middleware/
└── tests/
    ├── InvestmentTracker.Domain.Tests/
    ├── InvestmentTracker.Application.Tests/
    └── InvestmentTracker.API.Tests/

frontend/
├── src/
│   ├── components/
│   │   ├── common/
│   │   ├── layout/
│   │   ├── portfolio/
│   │   ├── transactions/
│   │   └── dashboard/        # NEW: Dashboard components
│   ├── pages/
│   ├── services/
│   ├── hooks/
│   └── types/
└── tests/
```

**Structure Decision**: Web application with separate backend and frontend projects. Clean Architecture layers in backend. Feature-based component organization in frontend.

## Implementation Phases

### Phase A: Core Portfolio (COMPLETE)
- User authentication (JWT)
- Portfolio CRUD
- Stock transactions (Buy/Sell)
- Position calculations
- Currency ledger

### Phase B: Real-time Quotes (COMPLETE)
- Stock quote API integration (TW/US/UK)
- Exchange rate fetching
- Quote caching in localStorage
- Batch quote refresh

### Phase C: Dashboard Analytics (NEW)
- **C1**: Historical Price API integration
  - Yahoo Finance for US/UK
  - TWSE/TPEx for Taiwan
  - Permanent caching in database
- **C2**: Historical Returns Calculation
  - Year-end valuation fetch
  - Per-position annual returns
  - Portfolio annual returns
  - YTD return calculation
- **C3**: CAPE Market Context
  - Research Affiliates API integration
  - 24-hour cache
  - Display with valuation context
- **C4**: Dashboard UI Redesign
  - Market context section
  - Historical returns table
  - Position allocation weights

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| External API for historical prices | Cannot calculate historical returns without historical prices | Manual input would require significant user effort annually |
| New database table (historical_prices) | Permanent cache for immutable historical data | localStorage insufficient for persistence across devices |

## New Requirements Summary (FR-030 to FR-039)

| Requirement | Description | Implementation |
|-------------|-------------|----------------|
| FR-030 | Display Global CAPE | Frontend fetch from RA API |
| FR-031 | Cache CAPE (24hr) | localStorage with timestamp |
| FR-032 | Portfolio annual returns | Calculate from year-end values |
| FR-033 | Position annual returns | Calculate from year-end values |
| FR-034 | Allocation weights | Value / Total Value × 100 |
| FR-035 | YTD return | Jan 1 value vs current |
| FR-036 | Return formula | ((End - Start - Contrib) / Start) × 100 |
| FR-037 | Fetch historical prices | Yahoo/TWSE API |
| FR-038 | Cache historical prices | Database (permanent) |
| FR-039 | Use Dec 31 closing | Or last trading day |
