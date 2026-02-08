# Implementation Plan: UI Enhancements Batch

**Branch**: `007-ui-enhancements` | **Date**: 2026-02-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/007-ui-enhancements/spec.md`

## Summary

This batch includes three UI improvements: (1) Fix stock trading dialog flicker by removing the Linked Ledger section and compacting the notes field, (2) Replace ledger overview page with dropdown navigation similar to portfolio selector, (3) Add CSV export/import for bank accounts following existing patterns.

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x + React 18 (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, Vite, TanStack Query, Tailwind CSS
**Storage**: PostgreSQL
**Testing**: xUnit (backend), Vitest + React Testing Library (frontend)
**Target Platform**: Web (Docker-hosted, self-hosted friendly)
**Project Type**: Web application (frontend + backend)
**Performance Goals**: Dialog render <500ms, ledger switch <200ms perceived latency
**Constraints**: Self-hosted on NAS/VPS (<512MB RAM idle)
**Scale/Scope**: Single user, ~10 bank accounts, ~5 ledgers typical

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✅ PASS | Export/import uses existing service patterns |
| II. Multi-Tenancy | ✅ PASS | All queries already filter by user context |
| III. Accuracy First | ✅ PASS | No financial calculations affected; decimal types preserved in export |
| IV. Self-Hosted Friendly | ✅ PASS | No new external dependencies |
| V. Technology Stack | ✅ PASS | Uses existing React/C#/.NET stack |

**Gate Result**: PASS - No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/007-ui-enhancements/
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
│   │   └── Entities/BankAccount.cs          # Existing entity
│   ├── InvestmentTracker.Infrastructure/
│   │   └── Persistence/                      # Repository patterns
│   └── InvestmentTracker.Api/
│       └── Controllers/BankAccountsController.cs  # Add export/import endpoints
└── tests/

frontend/
├── src/
│   ├── components/
│   │   ├── transactions/TransactionForm.tsx      # Remove Linked Ledger section
│   │   ├── ledger/LedgerSelector.tsx             # NEW: Dropdown component
│   │   └── import/BankAccountImportButton.tsx    # NEW: Import component
│   ├── contexts/
│   │   └── LedgerContext.tsx                     # NEW: Ledger state management
│   ├── pages/
│   │   ├── Currency.tsx                          # Refactor to use LedgerContext
│   │   └── CurrencyDetail.tsx                    # Integrate LedgerSelector
│   └── services/
│       └── csvExport.ts                          # Add bank account export
└── tests/
```

**Structure Decision**: Web application structure with frontend/backend separation. New components follow existing patterns in the codebase.

## Complexity Tracking

> No violations to justify - all gates passed.
