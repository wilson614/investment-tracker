# Implementation Plan: Ledger Exchange Cost Integration & Navigation Improvement

**Branch**: `009-ledger-cost-nav` | **Date**: 2026-02-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/009-ledger-cost-nav/spec.md`

## Summary

Integrate the existing LIFO weighted-average exchange cost calculation (`CurrencyLedgerService.CalculateExchangeRateForPurchase`) into the stock transaction creation flow (`CreateStockTransactionUseCase`), replace the AutoDeposit Deposit-creation with a three-option insufficient balance dialog (Margin / Top Up / Cancel), hide the manual exchange rate input on the transaction form (show as read-only), and fix the ledger navigation button to remember the last-selected ledger.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Backend), TypeScript 5.x / React 18 (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, Vite, TanStack Query, Tailwind CSS
**Storage**: PostgreSQL via Entity Framework Core
**Testing**: xUnit (backend), Vitest + React Testing Library (frontend)
**Target Platform**: Linux Docker containers (self-hosted)
**Project Type**: Web application (backend API + frontend SPA)
**Performance Goals**: Standard web app responsiveness; LIFO calculation is O(n) over ledger transactions per purchase
**Constraints**: < 512MB RAM idle; all monetary calculations in `decimal` type
**Scale/Scope**: Single-user to small family use; ~100s of transactions per ledger

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | **PASS** | LIFO logic stays in Domain layer (`CurrencyLedgerService`). Integration in Application layer (`CreateStockTransactionUseCase`). New API endpoint in API layer. Frontend changes in components. No layer violations. |
| II. Multi-Tenancy | **PASS** | All queries remain user-scoped. No new data access patterns bypass tenant filtering. Currency ledger already filtered by portfolio ownership. |
| III. Accuracy First | **PASS** | Uses existing `decimal`-based LIFO calculation. Weighted blend uses `decimal` arithmetic. New calculations need unit tests with edge cases. |
| IV. Self-Hosted Friendly | **PASS** | No new external dependencies. Uses existing `ITransactionDateExchangeRateService` for market rate fallback. |
| V. Technology Stack | **PASS** | C# .NET 8 backend, React/TypeScript frontend, PostgreSQL, EF Core. All aligned. |

**Technical Constraints:**
- Database Standards: **PASS** — No new tables. Existing `CurrencyTransaction` entity used with different `TransactionType` value.
- API Standards: **PASS** — New preview endpoint follows existing REST patterns. Error responses use existing `BusinessRuleException` structure.
- Security Requirements: **PASS** — No new security concerns. Existing auth/authz applies.

**Quality Standards:**
- Testing: Unit tests required for weighted blend calculation, LIFO integration in use case, AutoDeposit replacement logic.
- Code Quality: New enum values need XML documentation.

**All gates pass. No violations.**

## Project Structure

### Documentation (this feature)

```text
specs/009-ledger-cost-nav/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions
├── data-model.md        # Phase 1: data model changes
├── quickstart.md        # Phase 1: implementation guide
├── contracts/           # Phase 1: API contract changes
│   └── api-changes.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.API/
│   │   ├── Controllers/StockTransactionsController.cs    # Existing - no changes needed
│   │   ├── Controllers/CurrencyController.cs             # Existing - add preview endpoint
│   │   └── Dtos/RequestDtos.cs                           # Modify: replace AutoDeposit with BalanceAction
│   ├── InvestmentTracker.Application/
│   │   ├── DTOs/RequestDtos.cs                           # Modify: CreateStockTransactionRequest
│   │   └── UseCases/StockTransactions/
│   │       ├── CreateStockTransactionUseCase.cs           # Major changes: LIFO integration, balance handling
│   │       └── StockTransactionLinking.cs                 # Review: may need adjustments
│   ├── InvestmentTracker.Domain/
│   │   ├── Enums/BalanceAction.cs                         # New enum: None, Margin, TopUp
│   │   └── Services/CurrencyLedgerService.cs              # Existing: add weighted blend method
│   └── InvestmentTracker.Infrastructure/
│       └── (no changes expected)
└── tests/
    ├── InvestmentTracker.Domain.Tests/Services/
    │   └── CurrencyLedgerServiceTests.cs                  # Add weighted blend tests
    └── InvestmentTracker.Application.Tests/UseCases/StockTransactions/
        └── CreateStockTransactionUseCaseTests.cs           # New: LIFO integration + balance action tests

frontend/
├── src/
│   ├── api/                                               # Add LIFO rate preview API call
│   ├── components/
│   │   ├── layout/Navigation.tsx                          # Modify: ledger nav remembers last selection
│   │   └── transactions/TransactionForm.tsx               # Major changes: read-only rate, 3-option modal
│   └── types/index.ts                                     # Add BalanceAction type
└── (no new test files expected - existing patterns)
```

**Structure Decision**: Existing web application structure. No new projects or directories needed. Changes are modifications to existing files plus one new enum file.

## Complexity Tracking

> No constitution violations. No complexity tracking needed.
