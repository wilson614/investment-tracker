# Tasks: Family Investment Portfolio Tracker

**Input**: Design documents from `/specs/001-portfolio-tracker/`
**Prerequisites**: plan.md (required), spec.md (required), data-model.md, contracts/openapi.yaml, research.md, quickstart.md

**Tests**: Tests are included as the constitution mandates >80% coverage for domain logic and 100% for financial calculations.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `backend/src/InvestmentTracker.{Layer}/`
- **Frontend**: `frontend/src/`
- **Docker**: `docker/`
- **Tests**: `backend/tests/InvestmentTracker.{Layer}.Tests/`

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create project structure, configure tooling, and establish development environment

- [x] T001 Create solution structure with 4 backend projects in backend/InvestmentTracker.sln
- [x] T002 [P] Initialize InvestmentTracker.Domain class library in backend/src/InvestmentTracker.Domain/
- [x] T003 [P] Initialize InvestmentTracker.Application class library in backend/src/InvestmentTracker.Application/
- [x] T004 [P] Initialize InvestmentTracker.Infrastructure class library in backend/src/InvestmentTracker.Infrastructure/
- [x] T005 [P] Initialize InvestmentTracker.API web project in backend/src/InvestmentTracker.API/
- [x] T006 [P] Initialize test projects in backend/tests/ (Domain.Tests, Application.Tests, API.Tests)
- [x] T007 Create React TypeScript project with Vite in frontend/
- [x] T008 [P] Configure Tailwind CSS in frontend/tailwind.config.js
- [x] T009 [P] Configure Docker Compose in docker/docker-compose.yml (postgres, backend, frontend)
- [x] T010 [P] Create backend.Dockerfile with multi-stage build in docker/backend.Dockerfile
- [x] T011 [P] Create frontend.Dockerfile with nginx in docker/frontend.Dockerfile
- [x] T012 Create .env.example with required environment variables in project root

**Checkpoint**: Project scaffolding complete - all projects build successfully

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Domain Layer Foundation

- [x] T013 [P] Create IHasTimestamps interface in backend/src/InvestmentTracker.Domain/Common/IHasTimestamps.cs
- [x] T014 [P] Create BaseEntity abstract class in backend/src/InvestmentTracker.Domain/Common/BaseEntity.cs
- [x] T015 [P] Create Money value object in backend/src/InvestmentTracker.Domain/ValueObjects/Money.cs
- [x] T016 [P] Create ExchangeRate value object in backend/src/InvestmentTracker.Domain/ValueObjects/ExchangeRate.cs
- [x] T017 Create User entity in backend/src/InvestmentTracker.Domain/Entities/User.cs
- [x] T018 [P] Create TransactionType enum in backend/src/InvestmentTracker.Domain/Enums/TransactionType.cs
- [x] T019 [P] Create CurrencyTransactionType enum in backend/src/InvestmentTracker.Domain/Enums/CurrencyTransactionType.cs
- [x] T020 [P] Create FundSource enum in backend/src/InvestmentTracker.Domain/Enums/FundSource.cs

### Infrastructure Layer Foundation

- [x] T021 Create AppDbContext with audit timestamps in backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs
- [x] T022 [P] Create UserConfiguration for EF Core in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/UserConfiguration.cs
- [x] T023 Create ICurrentUserService interface in backend/src/InvestmentTracker.Application/Interfaces/ICurrentUserService.cs
- [x] T024 Create CurrentUserService implementation in backend/src/InvestmentTracker.Infrastructure/Services/CurrentUserService.cs

### Authentication Infrastructure

- [x] T025 Create RefreshToken entity in backend/src/InvestmentTracker.Domain/Entities/RefreshToken.cs
- [x] T026 [P] Create RefreshTokenConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs
- [x] T027 Create IJwtTokenService interface in backend/src/InvestmentTracker.Application/Interfaces/IJwtTokenService.cs
- [x] T028 Create JwtTokenService with Argon2 in backend/src/InvestmentTracker.Infrastructure/Services/JwtTokenService.cs
- [x] T029 Create AuthController (register, login, refresh, logout) in backend/src/InvestmentTracker.API/Controllers/AuthController.cs
- [x] T030 Configure JWT authentication in backend/src/InvestmentTracker.API/Program.cs
- [x] T031 Create TenantContextMiddleware for user context in backend/src/InvestmentTracker.API/Middleware/TenantContextMiddleware.cs

### Database Migration

- [x] T032 Create initial EF Core migration in backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/

### Frontend Foundation

- [x] T033 Create API client service in frontend/src/services/api.ts
- [x] T034 [P] Create auth context and hooks in frontend/src/hooks/useAuth.tsx
- [x] T035 [P] Create type definitions in frontend/src/types/index.ts
- [x] T036 Create Login page component in frontend/src/pages/Login.tsx
- [x] T037 Create protected route wrapper in frontend/src/components/common/ProtectedRoute.tsx
- [x] T038 Configure React Router and app structure in frontend/src/App.tsx

**Checkpoint**: Foundation ready - authentication works, user can login/register

---

## Phase 3: User Story 1 - Record Stock Purchase (Priority: P1) 🎯 MVP

**Goal**: Users can record stock/ETF buy transactions with accurate cost calculations

**Independent Test**: Enter a buy transaction and verify cost calculations match expected values

### Tests for User Story 1

- [x] T039 [P] [US1] Create PortfolioCalculator unit tests in backend/tests/InvestmentTracker.Domain.Tests/Services/PortfolioCalculatorTests.cs
- [x] T040 [P] [US1] Create StockTransaction validation tests in backend/tests/InvestmentTracker.Domain.Tests/Entities/StockTransactionTests.cs

### Domain Layer (US1)

- [x] T041 [P] [US1] Create Portfolio entity in backend/src/InvestmentTracker.Domain/Entities/Portfolio.cs
- [x] T042 [P] [US1] Create StockTransaction entity in backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs
- [x] T043 [US1] Create PortfolioCalculator domain service (RecalculatePosition, MovingAverageCost) in backend/src/InvestmentTracker.Domain/Services/PortfolioCalculator.cs

### Infrastructure Layer (US1)

- [x] T044 [P] [US1] Create PortfolioConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/PortfolioConfiguration.cs
- [x] T045 [P] [US1] Create StockTransactionConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/StockTransactionConfiguration.cs
- [x] T046 [US1] Create IPortfolioRepository interface in backend/src/InvestmentTracker.Domain/Interfaces/IPortfolioRepository.cs
- [x] T047 [US1] Create PortfolioRepository in backend/src/InvestmentTracker.Infrastructure/Repositories/PortfolioRepository.cs
- [x] T048 [US1] Add Portfolio and StockTransaction to AppDbContext and create migration

### Application Layer (US1)

- [x] T049 [P] [US1] Create PortfolioDto, StockTransactionDto in backend/src/InvestmentTracker.Application/DTOs/PortfolioDtos.cs
- [x] T050 [P] [US1] Create CreatePortfolioRequest, CreateStockTransactionRequest in backend/src/InvestmentTracker.Application/DTOs/RequestDtos.cs
- [x] T051 [US1] Create CreateStockTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs
- [x] T052 [US1] Create UpdateStockTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/UpdateStockTransactionUseCase.cs
- [x] T053 [US1] Create DeleteStockTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/DeleteStockTransactionUseCase.cs
- [x] T054 [US1] Create GetPortfolioSummaryUseCase in backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetPortfolioSummaryUseCase.cs

### API Layer (US1)

- [x] T055 [US1] Create PortfoliosController (CRUD, summary) in backend/src/InvestmentTracker.API/Controllers/PortfoliosController.cs
- [x] T056 [US1] Create StockTransactionsController (CRUD) in backend/src/InvestmentTracker.API/Controllers/StockTransactionsController.cs

### Frontend (US1)

- [x] T057 [P] [US1] Create TransactionForm component in frontend/src/components/transactions/TransactionForm.tsx
- [x] T058 [P] [US1] Create TransactionList component in frontend/src/components/transactions/TransactionList.tsx
- [x] T059 [P] [US1] Create PositionCard component in frontend/src/components/portfolio/PositionCard.tsx
- [x] T060 [US1] Create Portfolio page in frontend/src/pages/Portfolio.tsx
- [x] T061 [US1] Create Transactions page in frontend/src/pages/Transactions.tsx
- [x] T062 [US1] Add portfolio and transaction API calls in frontend/src/services/api.ts

**Checkpoint**: User Story 1 complete - can record buy transactions and see positions with correct costs

---

## Phase 4: User Story 2 - Manage Foreign Currency Ledger (Priority: P2)

**Goal**: Users can track foreign currency holdings with weighted average cost

**Independent Test**: Record currency exchanges and verify weighted average rate updates correctly

### Tests for User Story 2

- [x] T063 [P] [US2] Create CurrencyLedgerService unit tests in backend/tests/InvestmentTracker.Domain.Tests/Services/CurrencyLedgerServiceTests.cs
- [x] T064 [P] [US2] Create weighted average formula tests (ExchangeBuy, ExchangeSell, Interest) in backend/tests/InvestmentTracker.Domain.Tests/Services/WeightedAverageCostTests.cs

### Domain Layer (US2)

- [x] T065 [P] [US2] Create CurrencyLedger entity in backend/src/InvestmentTracker.Domain/Entities/CurrencyLedger.cs
- [x] T066 [P] [US2] Create CurrencyTransaction entity in backend/src/InvestmentTracker.Domain/Entities/CurrencyTransaction.cs
- [x] T067 [US2] Create CurrencyLedgerService domain service in backend/src/InvestmentTracker.Domain/Services/CurrencyLedgerService.cs

### Infrastructure Layer (US2)

- [x] T068 [P] [US2] Create CurrencyLedgerConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/CurrencyLedgerConfiguration.cs
- [x] T069 [P] [US2] Create CurrencyTransactionConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/CurrencyTransactionConfiguration.cs
- [x] T070 [US2] Create ICurrencyLedgerRepository interface in backend/src/InvestmentTracker.Domain/Interfaces/ICurrencyLedgerRepository.cs
- [x] T071 [US2] Create CurrencyLedgerRepository in backend/src/InvestmentTracker.Infrastructure/Repositories/CurrencyLedgerRepository.cs
- [x] T072 [US2] Add CurrencyLedger and CurrencyTransaction to AppDbContext and create migration

### Application Layer (US2)

- [x] T073 [P] [US2] Create CurrencyLedgerDto, CurrencyTransactionDto in backend/src/InvestmentTracker.Application/DTOs/CurrencyDtos.cs
- [x] T074 [US2] Create CreateCurrencyTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/CreateCurrencyTransactionUseCase.cs
- [x] T075 [US2] Create UpdateCurrencyTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/UpdateCurrencyTransactionUseCase.cs
- [x] T076 [US2] Create DeleteCurrencyTransactionUseCase in backend/src/InvestmentTracker.Application/UseCases/CurrencyTransactions/DeleteCurrencyTransactionUseCase.cs

### API Layer (US2)

- [x] T077 [US2] Create CurrencyLedgersController in backend/src/InvestmentTracker.API/Controllers/CurrencyLedgersController.cs
- [x] T078 [US2] Create CurrencyTransactionsController in backend/src/InvestmentTracker.API/Controllers/CurrencyTransactionsController.cs

### Frontend (US2)

- [x] T079 [P] [US2] Create CurrencyLedgerCard component in frontend/src/components/currency/CurrencyLedgerCard.tsx
- [x] T080 [P] [US2] Create CurrencyTransactionForm component in frontend/src/components/currency/CurrencyTransactionForm.tsx
- [x] T081 [P] [US2] Create CurrencyTransactionList component in frontend/src/components/currency/CurrencyTransactionList.tsx
- [x] T082 [US2] Create CurrencyLedger page in frontend/src/pages/CurrencyLedger.tsx
- [x] T083 [US2] Add currency ledger API calls in frontend/src/services/api.ts

**Checkpoint**: User Story 2 complete - can manage currency ledger with correct weighted average calculations

---

## Phase 5: User Story 3 - Buy Stock Using Currency Ledger (Priority: P3)

**Goal**: Link stock purchases to currency ledger with atomic transaction

**Independent Test**: Buy stock using currency ledger and verify both balances update atomically

### Tests for User Story 3

- [ ] T084 [P] [US3] Create atomic transaction integration tests in backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs

### Domain Layer (US3)

- [ ] T085 [US3] Add CurrencyLedgerId and FundSource to StockTransaction entity in backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs

### Application Layer (US3)

- [ ] T086 [US3] Update CreateStockTransactionUseCase with atomic currency deduction in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs
- [ ] T087 [US3] Add balance validation before stock purchase in CreateStockTransactionUseCase

### Infrastructure Layer (US3)

- [ ] T088 [US3] Update StockTransactionConfiguration with CurrencyLedger relationship
- [ ] T089 [US3] Create migration for FundSource and CurrencyLedgerId fields

### Frontend (US3)

- [ ] T090 [US3] Add fund source selector to TransactionForm in frontend/src/components/transactions/TransactionForm.tsx
- [ ] T091 [US3] Add currency ledger balance display in transaction form

**Checkpoint**: User Story 3 complete - stock purchases can use currency ledger with atomic deduction

---

## Phase 6: User Story 4 - View Portfolio Performance (Priority: P4)

**Goal**: Display performance metrics (XIRR, Unrealized PnL, Average Cost)

**Independent Test**: Record transactions and verify metrics match expected calculations

### Tests for User Story 4

- [ ] T092 [P] [US4] Create XIRR calculation unit tests in backend/tests/InvestmentTracker.Domain.Tests/Services/XirrCalculatorTests.cs
- [ ] T093 [P] [US4] Create Unrealized PnL calculation tests in backend/tests/InvestmentTracker.Domain.Tests/Services/UnrealizedPnlTests.cs

### Domain Layer (US4)

- [ ] T094 [US4] Implement XIRR calculator (Newton-Raphson) in backend/src/InvestmentTracker.Domain/Services/PortfolioCalculator.cs
- [ ] T095 [US4] Implement Unrealized PnL calculation in PortfolioCalculator

### Application Layer (US4)

- [ ] T096 [US4] Create CalculateXirrUseCase in backend/src/InvestmentTracker.Application/UseCases/Portfolio/CalculateXirrUseCase.cs
- [ ] T097 [US4] Update GetPortfolioSummaryUseCase with current prices and unrealized PnL

### API Layer (US4)

- [ ] T098 [US4] Add XIRR endpoint to PortfoliosController

### Frontend (US4)

- [ ] T099 [P] [US4] Create PerformanceMetrics component in frontend/src/components/portfolio/PerformanceMetrics.tsx
- [ ] T100 [P] [US4] Create CurrentPriceInput component in frontend/src/components/portfolio/CurrentPriceInput.tsx
- [ ] T101 [US4] Create Dashboard page with portfolio overview in frontend/src/pages/Dashboard.tsx
- [ ] T102 [US4] Update Portfolio page with performance metrics display

**Checkpoint**: User Story 4 complete - can view XIRR, unrealized PnL, and average cost

---

## Phase 7: User Story 5 - Sell Stock and Calculate Realized PnL (Priority: P5)

**Goal**: Calculate and display realized profit/loss when selling stocks

**Independent Test**: Sell shares and verify realized PnL matches expected calculation

### Tests for User Story 5

- [ ] T103 [P] [US5] Create Realized PnL calculation tests in backend/tests/InvestmentTracker.Domain.Tests/Services/RealizedPnlTests.cs

### Domain Layer (US5)

- [ ] T104 [US5] Implement Realized PnL calculation (average cost method) in PortfolioCalculator
- [ ] T105 [US5] Add share balance validation for sell transactions

### Application Layer (US5)

- [ ] T106 [US5] Update CreateStockTransactionUseCase with sell validation and realized PnL calculation

### Frontend (US5)

- [ ] T107 [P] [US5] Create RealizedPnlDisplay component in frontend/src/components/portfolio/RealizedPnlDisplay.tsx
- [ ] T108 [US5] Add realized PnL to transaction list and details

**Checkpoint**: User Story 5 complete - can sell stocks and see realized profit/loss

---

## Phase 8: User Story 6 - Multi-Tenancy and Data Isolation (Priority: P6)

**Goal**: Ensure each user sees only their own data

**Independent Test**: Login as different users and verify data isolation

### Tests for User Story 6

- [ ] T109 [P] [US6] Create data isolation integration tests in backend/tests/InvestmentTracker.API.Tests/Integration/MultiTenancyTests.cs

### Infrastructure Layer (US6)

- [ ] T110 [US6] Add global query filters for Portfolio in AppDbContext
- [ ] T111 [US6] Add global query filters for CurrencyLedger in AppDbContext
- [ ] T112 [US6] Add global query filters for StockTransaction in AppDbContext
- [ ] T113 [US6] Add global query filters for CurrencyTransaction in AppDbContext

### API Layer (US6)

- [ ] T114 [US6] Add ownership validation to all controllers
- [ ] T115 [US6] Verify TenantContextMiddleware sets user context correctly

**Checkpoint**: User Story 6 complete - data isolation verified between users

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T116 [P] Add Swagger/OpenAPI documentation in backend/src/InvestmentTracker.API/Program.cs
- [ ] T117 [P] Add global exception handling middleware in backend/src/InvestmentTracker.API/Middleware/ExceptionHandlingMiddleware.cs
- [ ] T118 [P] Add request validation with FluentValidation
- [ ] T119 [P] Add structured logging configuration
- [ ] T120 [P] Create responsive navigation component in frontend/src/components/common/Navigation.tsx
- [ ] T121 [P] Add loading states and error handling to frontend components
- [ ] T122 [P] Add toast notifications for user feedback
- [ ] T123 Run quickstart.md validation scenarios
- [ ] T124 Performance testing with 10,000 transactions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies - can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 - BLOCKS all user stories
- **User Stories (Phase 3-8)**: All depend on Phase 2 completion
  - US1 → Independent (MVP)
  - US2 → Independent
  - US3 → Depends on US1 + US2 (integration)
  - US4 → Depends on US1 (needs transactions)
  - US5 → Depends on US1 (needs buy transactions)
  - US6 → Can run parallel to US1-US5 (infrastructure)
- **Phase 9 (Polish)**: Depends on all user stories

### User Story Dependencies

```
US1 (Stock Purchase) ─────┐
                          ├─→ US3 (Integration)
US2 (Currency Ledger) ────┘

US1 ──→ US4 (Performance) ──→ US5 (Sell/Realized PnL)

US6 (Multi-Tenancy) ── runs parallel, integrates with all
```

### Parallel Opportunities per Phase

**Phase 1**: T002-T006 (projects), T009-T011 (Docker)
**Phase 2**: T013-T020 (domain), T022+T026 (configs)
**US1**: T039-T040 (tests), T041-T042 (entities), T044-T045 (configs), T057-T059 (frontend)
**US2**: T063-T064 (tests), T065-T066 (entities), T068-T069 (configs), T079-T081 (frontend)
**US4**: T092-T093 (tests), T099-T100 (frontend)

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (authentication, base entities)
3. Complete Phase 3: User Story 1 (record stock purchase)
4. **STOP and VALIDATE**: Test transaction recording independently
5. Deploy/demo if ready - this is the MVP!

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add User Story 1 → Test → Deploy (MVP!)
3. Add User Story 2 → Test → Deploy
4. Add User Story 3 → Test → Deploy (Integration complete)
5. Add User Story 4 → Test → Deploy (Performance metrics)
6. Add User Story 5 → Test → Deploy (Full transaction lifecycle)
7. Add User Story 6 → Test → Deploy (Production-ready multi-tenancy)
8. Polish phase → Final release

---

## Notes

- [P] tasks = different files, no dependencies within same phase
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All monetary values use `decimal` type (constitution mandate)
- All entities include CreatedAt/UpdatedAt timestamps (constitution mandate)
