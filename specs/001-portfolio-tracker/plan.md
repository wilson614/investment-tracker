# Implementation Plan: Family Investment Portfolio Tracker

**Branch**: `001-portfolio-tracker` | **Date**: 2026-01-06 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-portfolio-tracker/spec.md`

## Summary

Build a family investment portfolio tracking system to replace manual spreadsheets. The system
tracks ETF/stock transactions with multi-currency support, calculates performance metrics
(XIRR, Unrealized/Realized PnL), and maintains a foreign currency ledger with weighted average
cost methodology. Implements strict multi-tenancy for family member data isolation.

**Technical Approach**: Clean Architecture with .NET 8 Web API backend, React TypeScript
frontend, PostgreSQL database, all containerized via Docker for self-hosted deployment.

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**:
- Backend: ASP.NET Core 8, Entity Framework Core 8, JWT Bearer Authentication
- Frontend: React 18, Tailwind CSS, React Query (TanStack Query)
**Storage**: PostgreSQL 16 (Docker container), EF Core Migrations
**Testing**: xUnit + FluentAssertions (Backend), Jest + React Testing Library (Frontend)
**Target Platform**: Docker containers (Linux-based), self-hosted NAS/VPS
**Project Type**: Web application (frontend + backend)
**Performance Goals**: Pages load <3s, metrics update <5s, support 10,000 transactions/user
**Constraints**: <512MB RAM idle, <2GB under load, fully offline after deployment
**Scale/Scope**: 10 concurrent family members, ~10,000 transactions per user

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| **I. Clean Architecture** | ✅ PASS | 4-layer structure: Domain, Application, Infrastructure, API. Domain has no external dependencies. |
| **II. Multi-Tenancy** | ✅ PASS | FR-017~FR-019 mandate user data isolation. All queries include UserId filtering. |
| **III. Accuracy First** | ✅ PASS | `decimal` type for all monetary values. XIRR with Newton-Raphson. 4 decimal precision. Unit tests for all calculations. |
| **IV. Self-Hosted Friendly** | ✅ PASS | Single `docker-compose.yml`, PostgreSQL, environment-variable config, no cloud dependencies. |
| **V. Technology Stack** | ✅ PASS | C# .NET 8, React TypeScript, PostgreSQL, EF Core, xUnit, Docker. |
| **Database Standards** | ✅ PASS | All entities include CreatedAt/UpdatedAt. Soft deletes for financial records. |
| **API Standards** | ✅ PASS | JWT auth, consistent error responses, API versioning via URL prefix. |
| **Security Requirements** | ✅ PASS | Argon2 password hashing, environment-based config, input validation at API layer. |

**Gate Result**: ✅ All principles satisfied. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/001-portfolio-tracker/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI specs)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
# Clean Architecture - .NET 8 Backend
backend/
├── src/
│   ├── InvestmentTracker.Domain/           # Entities, Value Objects, Domain Services
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── Portfolio.cs
│   │   │   ├── StockTransaction.cs
│   │   │   ├── Position.cs
│   │   │   ├── CurrencyLedger.cs
│   │   │   └── CurrencyTransaction.cs
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs
│   │   │   └── ExchangeRate.cs
│   │   ├── Services/
│   │   │   ├── PortfolioCalculator.cs      # Weighted avg, PnL, XIRR
│   │   │   └── CurrencyLedgerService.cs
│   │   └── Interfaces/
│   │       └── IRepository.cs
│   │
│   ├── InvestmentTracker.Application/      # Use Cases, DTOs, Validators
│   │   ├── UseCases/
│   │   │   ├── StockTransactions/
│   │   │   ├── CurrencyTransactions/
│   │   │   └── Portfolio/
│   │   ├── DTOs/
│   │   └── Interfaces/
│   │
│   ├── InvestmentTracker.Infrastructure/   # EF Core, Repositories
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/
│   │   │   └── Migrations/
│   │   ├── Repositories/
│   │   ├── Services/
│   │   │   └── JwtTokenService.cs
│   │   └── StockPrices/                     # Real-time price fetching
│   │       ├── IStockPriceProvider.cs
│   │       ├── IStockPriceService.cs
│   │       ├── StockPriceService.cs
│   │       ├── SinaStockPriceProvider.cs    # US/UK via Sina Finance
│   │       ├── TwseStockPriceProvider.cs    # Taiwan via TWSE
│   │       └── SinaExchangeRateProvider.cs  # Exchange rates via Sina
│   │
│   └── InvestmentTracker.API/              # Controllers, Middleware
│       ├── Controllers/
│       │   ├── AuthController.cs
│       │   ├── PortfoliosController.cs
│       │   ├── StockTransactionsController.cs
│       │   ├── StockPricesController.cs     # Real-time price API
│       │   └── CurrencyController.cs
│       ├── Middleware/
│       │   └── TenantContextMiddleware.cs
│       └── Program.cs
│
└── tests/
    ├── InvestmentTracker.Domain.Tests/     # Unit tests for domain logic
    ├── InvestmentTracker.Application.Tests/
    └── InvestmentTracker.API.Tests/        # Integration tests

# React TypeScript Frontend
frontend/
├── src/
│   ├── components/
│   │   ├── common/
│   │   ├── portfolio/
│   │   │   ├── PositionCard.tsx          # Position with inline price fetch
│   │   │   ├── PerformanceMetrics.tsx
│   │   │   └── CurrentPriceInput.tsx     # (deprecated - moved to PositionCard)
│   │   ├── transactions/
│   │   └── currency/
│   ├── pages/
│   │   ├── Dashboard.tsx
│   │   ├── Portfolio.tsx                 # Fetch all prices button, XIRR
│   │   ├── Transactions.tsx
│   │   └── CurrencyLedger.tsx
│   ├── services/
│   │   └── api.ts                        # stockPriceApi with getQuoteWithRate
│   ├── hooks/
│   ├── types/
│   │   └── index.ts                      # StockQuoteResponse, ExchangeRateResponse
│   └── App.tsx
├── tests/
└── tailwind.config.js

# Docker Configuration
docker/
├── docker-compose.yml
├── backend.Dockerfile
└── frontend.Dockerfile
```

**Structure Decision**: Web application with Clean Architecture backend (4 layers) and
React frontend. Follows constitution's separation of concerns mandate.

## Complexity Tracking

> No violations to justify. All architecture decisions align with constitution principles.

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| 4-layer backend | Domain/Application/Infrastructure/API | Constitution mandates Clean Architecture |
| Repository pattern | Used for data access abstraction | Enables unit testing of domain logic |
| Domain Services | PortfolioCalculator, CurrencyLedgerService | Encapsulates complex business logic |
