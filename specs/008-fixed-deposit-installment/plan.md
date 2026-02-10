# Implementation Plan: Fixed Deposit and Credit Card Installment Tracking

**Branch**: `008-fixed-deposit-installment` | **Date**: 2026-02-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/008-fixed-deposit-installment/spec.md`

## Summary

Implement two related features to improve financial liquidity visibility:
1. **Fixed Deposits**: Track time deposits with principal, interest rate, term, and maturity date
2. **Credit Card Installments**: Track installment purchases and remaining unpaid balance

Both features contribute to a new "Available Funds" calculation that shows users their true liquid assets by subtracting committed funds (fixed deposits + unpaid installments) from total bank assets.

**Post-implementation note**: Fixed deposits were merged into `BankAccount` (`AccountType = FixedDeposit`) instead of keeping a standalone `FixedDeposit` aggregate/module.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, React 18, TanStack Query, Tailwind CSS
**Storage**: PostgreSQL with EF Core migrations
**Testing**: xUnit (Backend), Vitest + React Testing Library (Frontend)
**Target Platform**: Docker containers, self-hosted NAS/VPS
**Project Type**: Web application (backend + frontend)
**Performance Goals**: Dashboard load < 2 seconds, form submission < 1 second
**Constraints**: < 512MB RAM idle, offline-capable after deployment
**Scale/Scope**: Single-user to family use (< 10 concurrent users)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | PASS | New entities in Domain, repositories in Infrastructure, use cases in Application |
| II. Multi-Tenancy | PASS | All entities have UserId, queries filtered by current user |
| III. Accuracy First | PASS | Using decimal for all monetary values, interest calculations documented |
| IV. Self-Hosted Friendly | PASS | No external service dependencies, PostgreSQL only |
| V. Technology Stack | PASS | C# .NET 8 backend, React frontend, PostgreSQL |

## Project Structure

### Documentation (this feature)

```text
specs/008-fixed-deposit-installment/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── fixed-deposits.yaml
│   ├── credit-cards.yaml
│   ├── installments.yaml
│   └── available-funds.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── InvestmentTracker.Domain/
│   │   ├── Entities/
│   │   │   ├── BankAccount.cs           # MODIFIED (fixed deposit fields merged)
│   │   │   ├── CreditCard.cs            # NEW
│   │   │   └── Installment.cs           # NEW
│   │   ├── Enums/
│   │   │   ├── BankAccountType.cs       # MODIFIED (Savings / FixedDeposit)
│   │   │   ├── FixedDepositStatus.cs    # NEW (used by BankAccount)
│   │   │   └── InstallmentStatus.cs     # NEW
│   │   ├── Interfaces/
│   │   │   ├── IBankAccountRepository.cs    # EXISTING, reused for fixed deposits
│   │   │   ├── ICreditCardRepository.cs     # NEW
│   │   │   └── IInstallmentRepository.cs    # NEW
│   │   └── Services/
│   │       └── TotalAssetsService.cs        # MODIFIED (summary integration)
│   ├── InvestmentTracker.Application/
│   │   ├── DTOs/
│   │   │   ├── ResponseDtos.cs          # MODIFIED (BankAccount fixed-deposit fields)
│   │   │   ├── RequestDtos.cs           # MODIFIED (BankAccount fixed-deposit requests)
│   │   │   ├── CreditCardDto.cs         # NEW
│   │   │   └── InstallmentDto.cs        # NEW
│   │   └── UseCases/
│   │       ├── BankAccount/             # MODIFIED (fixed deposit create/update/close via bank account)
│   │       ├── CreditCards/             # NEW folder
│   │       ├── Installments/            # NEW folder
│   │       └── Assets/                  # MODIFIED (installment unpaid in total assets summary)
│   ├── InvestmentTracker.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   │   ├── BankAccountConfiguration.cs  # MODIFIED (merged fixed deposit columns)
│   │   │   │   ├── CreditCardConfiguration.cs   # NEW
│   │   │   │   └── InstallmentConfiguration.cs  # NEW
│   │   │   └── Migrations/
│   │   │       ├── *AddFixedDepositAndInstallment*.cs        # CREATED initial tables
│   │   │       ├── *MergeFixedDepositIntoBankAccount*.cs      # MERGE migration
│   │   │       ├── *RenameCreditCardBillingCycleDayToPaymentDueDay*.cs
│   │   │       └── *RenameStartDateToFirstPaymentDate*.cs
│   │   └── Repositories/
│   │       ├── BankAccountRepository.cs      # EXISTING, reused for fixed deposits
│   │       ├── CreditCardRepository.cs       # NEW
│   │       └── InstallmentRepository.cs      # NEW
│   └── InvestmentTracker.API/
│       └── Controllers/
│           ├── BankAccountsController.cs     # MODIFIED (fixed deposit operations under /api/bank-accounts)
│           ├── CreditCardsController.cs      # NEW
│           ├── InstallmentsController.cs     # NEW
│           └── AssetsController.cs           # MODIFIED (summary endpoint)

frontend/
└── src/
    └── features/
        ├── bank-accounts/           # MODIFIED: fixed-deposit UI integrated here
        │   ├── api/
        │   │   └── bankAccountsApi.ts
        │   ├── components/
        │   │   ├── BankAccountList/Card/Form (fixed + savings rendering)
        │   │   └── ...
        │   ├── hooks/
        │   │   └── useBankAccounts.ts
        │   ├── pages/
        │   │   └── BankAccountsPage.tsx
        │   └── types/
        │       └── index.ts
        ├── credit-cards/
        │   ├── api/
        │   │   ├── creditCardsApi.ts
        │   │   └── installmentsApi.ts
        │   ├── components/
        │   │   ├── CreditCardList/Form
        │   │   ├── InstallmentList/Form
        │   │   └── UpcomingPayments.tsx
        │   ├── hooks/
        │   │   ├── useCreditCards.ts
        │   │   └── useInstallments.ts
        │   └── types/
        │       ├── index.ts
        │       └── installment.ts
        └── total-assets/            # MODIFIED existing summary module
            └── components/
                └── NonDisposableAssetsSection.tsx  # includes installment unpaid balance
```

**Structure Decision**: Following existing web application pattern with separate backend (Clean Architecture) and frontend (feature-based) structures. Fixed deposits are integrated into the existing bank-account feature and API surface instead of a dedicated fixed-deposit module.

## Complexity Tracking

No constitution violations. Feature follows established patterns with no additional complexity.
