# Implementation Plan: Bank Account Enhancements

**Branch**: `006-bank-account-enhancements` | **Date**: 2026-02-06 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-bank-account-enhancements/spec.md`

## Summary

This feature adds multi-currency support for bank accounts, virtual fund allocations for mental accounting, generalizes historical performance calculations for multi-currency portfolios, standardizes currency display formatting, fixes the interest cap zero display bug, and **refactors the total assets dashboard with disposable fund tracking**.

**Key Changes:**
1. **P1**: Add `Currency` field to BankAccount entity with multi-currency support (7 currencies)
2. **P2**: New `FundAllocation` entity for virtual allocation of bank assets to purposes
3. **P2**: **NEW** - Refactor Total Assets Dashboard with core metrics (Investment Ratio, Stock Ratio) and disposable/non-disposable fund distinction
4. **P3**: Generalize `HistoricalPerformanceService` to handle any portfolio base currency
5. **P4**: Create unified currency formatting utility
6. **P5**: Fix `interestCap=0` display logic bug

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Backend), TypeScript 5.x / React 18 (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, Vite, TanStack Query, Tailwind CSS
**Storage**: PostgreSQL with EF Core migrations
**Testing**: xUnit (Backend), Vitest + React Testing Library (Frontend)
**Target Platform**: Docker containers (self-hosted NAS/VPS)
**Project Type**: Web application (backend + frontend)
**Performance Goals**: Standard web app response times (<500ms API, <100ms UI interactions)
**Constraints**: <512MB RAM idle, offline-capable after deployment
**Scale/Scope**: Single user/family, <100 bank accounts per user

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✅ PASS | New entities in Domain, Use Cases in Application, API in Controllers |
| II. Multi-Tenancy | ✅ PASS | All queries filter by UserId, FundAllocation owned by user |
| III. Accuracy First | ✅ PASS | Decimal types for all monetary values, exchange rate precision preserved |
| IV. Self-Hosted Friendly | ✅ PASS | No new external dependencies, exchange rates from existing service |
| V. Technology Stack | ✅ PASS | C#/.NET 8, React/TypeScript, PostgreSQL, EF Core |

**Database Standards**: ✅ Migrations include created_at/updated_at, soft delete not needed for FundAllocation
**API Standards**: ✅ RESTful endpoints, consistent error responses
**Security Requirements**: ✅ Input validation at API boundary, ownership checks

## Project Structure

### Documentation (this feature)

```text
specs/006-bank-account-enhancements/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   │   ├── BankAccount.cs          # MODIFY: Add Currency field
│   │   │   └── FundAllocation.cs       # NEW: Fund allocation entity (with IsDisposable)
│   │   ├── Enums/
│   │   │   └── AllocationPurpose.cs    # NEW: Purpose enum (EmergencyFund, FamilyDeposit, General, Savings, Investment, Other)
│   │   └── Services/
│   │       └── TotalAssetsService.cs   # MODIFY: Add fund allocation breakdown
│   ├── InvestmentTracker.Application/
│   │   ├── UseCases/
│   │   │   ├── BankAccount/            # MODIFY: Support Currency
│   │   │   └── FundAllocation/         # NEW: CRUD use cases
│   │   ├── Services/
│   │   │   └── HistoricalPerformanceService.cs  # MODIFY: Multi-currency support
│   │   └── DTOs/
│   │       └── ResponseDtos.cs         # MODIFY: Add currency, allocations
│   ├── InvestmentTracker.Infrastructure/
│   │   ├── Repositories/
│   │   │   └── FundAllocationRepository.cs  # NEW
│   │   └── Migrations/                 # NEW: Add Currency, FundAllocation table
│   └── InvestmentTracker.API/
│       └── Controllers/
│           └── FundAllocationsController.cs  # NEW
└── tests/
    └── unit/                           # NEW: Tests for new functionality

frontend/
├── src/
│   ├── features/
│   │   ├── bank-accounts/
│   │   │   ├── components/
│   │   │   │   ├── BankAccountCard.tsx     # MODIFY: Currency display, interestCap fix
│   │   │   │   └── BankAccountForm.tsx     # MODIFY: Currency selector
│   │   │   └── types/index.ts              # MODIFY: Add currency field
│   │   ├── fund-allocations/               # NEW: Fund allocation feature
│   │   │   ├── components/
│   │   │   │   ├── AllocationForm.tsx      # MODIFY: Add isDisposable toggle
│   │   │   │   └── AllocationSummary.tsx   # MODIFY: Show disposable status
│   │   │   ├── api/
│   │   │   └── types/                      # MODIFY: Add isDisposable field
│   │   └── total-assets/
│   │       ├── pages/
│   │       │   └── TotalAssetsDashboard.tsx  # MODIFY: New layout with core metrics
│   │       ├── components/
│   │       │   ├── TotalAssetsBanner.tsx     # KEEP: Grand total display
│   │       │   ├── CoreMetricsSection.tsx    # NEW: Investment Ratio + Stock Ratio
│   │       │   ├── MetricCard.tsx            # NEW: Reusable metric display
│   │       │   ├── DisposableAssetsSection.tsx   # NEW: Left 2/3 section
│   │       │   ├── NonDisposableAssetsSection.tsx # NEW: Right 1/3 section
│   │       │   ├── AssetsBreakdownPieChart.tsx   # MODIFY: 4-slice chart
│   │       │   └── AssetCategorySummary.tsx  # MODIFY: Investment + Disposable breakdown
│   │       └── types/index.ts              # MODIFY: Add new summary fields
│   └── utils/
│       └── currency.ts                     # NEW: Unified currency formatting
└── tests/
```

**Structure Decision**: Existing web application structure with Clean Architecture backend. New FundAllocation feature module in frontend, shared currency utility.

## Complexity Tracking

No constitution violations requiring justification. All changes follow existing patterns.
