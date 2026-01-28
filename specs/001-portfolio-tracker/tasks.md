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

## Phase 0: Portfolio-Ledger Binding & Auto-Linking (Story 0.2)

**Purpose**: Enforce mandatory 1:1 Portfolio-Ledger binding and simplify stock transaction linking (FR-001, FR-005).

- [x] T255 Enforce 1:1 Portfolio-Ledger binding (schema + API contracts)
- [x] T256 Add currency mismatch validation between stock and bound ledger (FR-005)
- [x] T257 Frontend: remove FundSource/ledger selection and rely on bound ledger auto-linking
- [x] T258 Remove any UI that allows changing the bound ledger (binding is permanent)

**Checkpoint**: Stock transactions always link to the portfolio's bound ledger; user cannot manually override the binding.

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

- [x] T084 [P] [US3] Create atomic transaction integration tests in backend/tests/InvestmentTracker.API.Tests/Integration/AtomicTransactionTests.cs

### Domain Layer (US3)

- [x] T085 [US3] Add CurrencyLedgerId and FundSource to StockTransaction entity in backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs

### Application Layer (US3)

- [x] T086 [US3] Update CreateStockTransactionUseCase with atomic currency deduction in backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs
- [x] T087 [US3] Add balance validation before stock purchase in CreateStockTransactionUseCase

### Infrastructure Layer (US3)

- [x] T088 [US3] Update StockTransactionConfiguration with CurrencyLedger relationship
- [x] T089 [US3] Create migration for FundSource and CurrencyLedgerId fields

### Frontend (US3)

- [x] T090 [US3] Add fund source selector to TransactionForm in frontend/src/components/transactions/TransactionForm.tsx
- [x] T091 [US3] Add currency ledger balance display in transaction form

**Checkpoint**: User Story 3 complete - stock purchases can use currency ledger with atomic deduction

---

## Phase 6: User Story 4 - View Portfolio Performance (Priority: P4)

**Goal**: Display performance metrics (XIRR, Unrealized PnL, Average Cost)

**Independent Test**: Record transactions and verify metrics match expected calculations

### Tests for User Story 4

- [x] T092 [P] [US4] Create XIRR calculation unit tests in backend/tests/InvestmentTracker.Domain.Tests/Services/XirrCalculatorTests.cs
- [x] T093 [P] [US4] Create Unrealized PnL calculation tests in backend/tests/InvestmentTracker.Domain.Tests/Services/UnrealizedPnlTests.cs

### Domain Layer (US4)

- [x] T094 [US4] Implement XIRR calculator (Newton-Raphson) in backend/src/InvestmentTracker.Domain/Services/PortfolioCalculator.cs
- [x] T095 [US4] Implement Unrealized PnL calculation in PortfolioCalculator

### Application Layer (US4)

- [x] T096 [US4] Create CalculateXirrUseCase in backend/src/InvestmentTracker.Application/UseCases/Portfolio/CalculateXirrUseCase.cs
- [x] T097 [US4] Update GetPortfolioSummaryUseCase with current prices and unrealized PnL

### API Layer (US4)

- [x] T098 [US4] Add XIRR endpoint to PortfoliosController

### Frontend (US4)

- [x] T099 [P] [US4] Create PerformanceMetrics component in frontend/src/components/portfolio/PerformanceMetrics.tsx
- [x] T100 [P] [US4] Create CurrentPriceInput component in frontend/src/components/portfolio/CurrentPriceInput.tsx
- [x] T101 [US4] Create Dashboard page with portfolio overview in frontend/src/pages/Dashboard.tsx
- [x] T102 [US4] Update Portfolio page with performance metrics display

**Checkpoint**: User Story 4 complete - can view XIRR, unrealized PnL, and average cost

---

## Phase 7: User Story 5 - Sell Stock and Calculate Realized PnL (Priority: P5)

**Goal**: Calculate and display realized profit/loss when selling stocks

**Independent Test**: Sell shares and verify realized PnL matches expected calculation

### Tests for User Story 5

- [x] T103 [P] [US5] Create Realized PnL calculation tests in backend/tests/InvestmentTracker.Domain.Tests/Services/PortfolioCalculatorTests.cs

### Domain Layer (US5)

- [x] T104 [US5] Implement Realized PnL calculation (average cost method) in PortfolioCalculator
- [x] T105 [US5] Add share balance validation for sell transactions

### Application Layer (US5)

- [x] T106 [US5] Update CreateStockTransactionUseCase with sell validation and realized PnL calculation

### Frontend (US5)

- [x] T107 [P] [US5] Add realizedPnlHome to StockTransaction type in frontend/src/types/index.ts
- [x] T108 [US5] Add realized PnL column to TransactionList component

**Checkpoint**: User Story 5 complete - can sell stocks and see realized profit/loss

---

## Phase 8: User Story 6 - Multi-Tenancy and Data Isolation (Priority: P6)

**Goal**: Ensure each user sees only their own data

**Independent Test**: Login as different users and verify data isolation

### Tests for User Story 6

- [x] T109 [P] [US6] Create data isolation integration tests in backend/tests/InvestmentTracker.API.Tests/Integration/MultiTenancyTests.cs

### Infrastructure Layer (US6)

- [x] T110 [US6] Add global query filters for Portfolio in AppDbContext
- [x] T111 [US6] Add global query filters for CurrencyLedger in AppDbContext
- [x] T112 [US6] Add global query filters for StockTransaction in AppDbContext (via Portfolio relationship)
- [x] T113 [US6] Add global query filters for CurrencyTransaction in AppDbContext (via CurrencyLedger relationship)

### API Layer (US6)

- [x] T114 [US6] Add ownership validation to all controllers and use cases
- [x] T115 [US6] Verify TenantContextMiddleware sets user context correctly

**Checkpoint**: User Story 6 complete - data isolation verified between users

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T116 [P] Add Swagger/OpenAPI documentation in backend/src/InvestmentTracker.API/Program.cs
- [x] T117 [P] Add global exception handling middleware in backend/src/InvestmentTracker.API/Middleware/ExceptionHandlingMiddleware.cs
- [x] T118 [P] Add request validation with FluentValidation
- [x] T119 [P] Add structured logging configuration (Serilog)
- [x] T120 [P] Create responsive navigation component in frontend/src/components/layout/Navigation.tsx
- [x] T121 [P] Add loading states and error handling to frontend components (LoadingSpinner, ErrorDisplay)
- [x] T122 [P] Add toast notifications for user feedback (ToastProvider)
- [-] T123 Run quickstart.md validation scenarios *(deferred: manual validation done during development)*
- [-] T124 Performance testing with 10,000 transactions *(deferred: not required for v1.0)*

**Checkpoint**: Phase 9 complete - cross-cutting concerns implemented

---

## Phase 10: Real-Time Data Integration (Required Advanced Feature)

**Purpose**: Integrate external APIs for real-time stock prices and exchange rates

**Note**: This is a required advanced feature that has been FULLY IMPLEMENTED.

### External API Integration

- [x] T125 [P] Research and select stock price API provider (Yahoo Finance, Alpha Vantage, etc.)
- [x] T126 [P] Research and select exchange rate API provider
- [x] T127 Create IStockPriceService interface in backend/src/InvestmentTracker.Infrastructure/StockPrices/StockPriceService.cs
- [x] T128 Create IExchangeRateProvider interface in backend/src/InvestmentTracker.Infrastructure/StockPrices/SinaExchangeRateProvider.cs
- [x] T129 Implement StockPriceService with caching in backend/src/InvestmentTracker.Infrastructure/StockPrices/StockPriceService.cs
- [x] T130 Implement ExchangeRateService with caching in backend/src/InvestmentTracker.Infrastructure/StockPrices/SinaExchangeRateProvider.cs

### API Endpoints

- [x] T131 Create PricesController for stock price lookups in backend/src/InvestmentTracker.API/Controllers/StockPricesController.cs
- [x] T132 Create ExchangeRatesController for rate lookups in backend/src/InvestmentTracker.API/Controllers/StockPricesController.cs (integrated)

### Frontend Integration

- [x] T133 Update PositionCard to auto-fetch prices in frontend/src/components/portfolio/PositionCard.tsx
- [x] T134 Update stockPriceApi to support exchange rate queries in frontend/src/services/api.ts
- [x] T135 Add price refresh button to Portfolio page in frontend/src/pages/Portfolio.tsx

**Checkpoint**: Real-time data integration complete - prices and exchange rates auto-populated

---

## Phase 11: UI Enhancement & Data Import

**Purpose**: Improve visual design, add Chinese localization, and enable CSV data import

### UI Localization

- [x] T136 [P] Localize UI to Traditional Chinese - scan and update all English text

### Visual Design Enhancement (Modern Professional Style)

- [x] T137 [P] Install Lucide Icons and update navigation/button icons
- [x] T138 [P] Design color palette and base style variables (CSS custom properties)
- [x] T139 Improve card, table, and form visual design
- [x] T140 [P] Install Recharts and create chart components
- [x] T141 Add portfolio distribution pie chart and performance line chart
- [x] T142 [P] Add page transition animations

### CSV Import Feature

- [x] T143 [P] Create CSV parser utility functions in frontend/src/utils/csvParser.ts
- [x] T144 [P] Create generic CSV import modal component in frontend/src/components/import/CSVImportModal.tsx
- [x] T145 Implement currency transaction CSV import (auto field mapping)
- [x] T146 Implement stock transaction CSV import (auto field mapping)
- [x] T147 [P] Create import preview and error validation display

**Checkpoint**: Phase 11 complete - UI modernized with Chinese localization and CSV import enabled

---

## Phase 12: User Story 7 - Dashboard with Historical Returns (Priority: P7)

**Goal**: Display historical performance and market context on dashboard

**Independent Test**: View dashboard after recording transactions spanning multiple years, verify historical returns are calculated correctly and CAPE data is displayed.

### Setup (Database & Infrastructure)

- [x] T148 [US7] Create HistoricalPrice entity in backend/src/InvestmentTracker.Domain/Entities/HistoricalPrice.cs *(implemented as HistoricalYearEndData in 002)*
- [x] T149 [US7] Add HistoricalPriceConfiguration in backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/HistoricalPriceConfiguration.cs *(implemented as HistoricalYearEndDataConfiguration in 002)*
- [x] T150 [US7] Add DbSet<HistoricalPrice> to AppDbContext in backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs *(implemented as HistoricalYearEndData in 002)*
- [x] T151 [US7] Create and apply EF Core migration for historical_prices table *(implemented as historical_year_end_data in 002)*

### Backend - Historical Price Service (C1)

- [x] T152 [P] [US7] Create IHistoricalPriceRepository interface in backend/src/InvestmentTracker.Application/Interfaces/IHistoricalPriceRepository.cs *(implemented as IHistoricalYearEndDataRepository in 002)*
- [x] T153 [P] [US7] Create IHistoricalPriceService interface in backend/src/InvestmentTracker.Application/Interfaces/IHistoricalPriceService.cs *(implemented as IHistoricalYearEndDataService in 002)*
- [x] T154 [US7] Implement HistoricalPriceRepository in backend/src/InvestmentTracker.Infrastructure/Repositories/HistoricalPriceRepository.cs *(implemented as HistoricalYearEndDataRepository in 002)*
- [x] T155 [US7] Implement YahooFinanceHistoricalService for US/UK stocks in backend/src/InvestmentTracker.Infrastructure/Services/YahooFinanceHistoricalService.cs *(implemented via StooqHistoricalPriceService in 002)*
- [x] T156 [US7] Implement TwseHistoricalService for Taiwan stocks in backend/src/InvestmentTracker.Infrastructure/Services/TwseHistoricalService.cs *(implemented as TwseStockHistoricalPriceService in 002)*
- [x] T157 [US7] Create HistoricalPriceService facade in backend/src/InvestmentTracker.Infrastructure/Services/HistoricalPriceService.cs *(implemented as HistoricalYearEndDataService in 002)*
- [x] T158 [US7] Register historical price services in DI container in backend/src/InvestmentTracker.API/Program.cs *(completed in 002)*

### Backend - Historical Returns Calculation (C2)

- [x] T159 [US7] Create HistoricalReturnDto and related DTOs in backend/src/InvestmentTracker.Application/DTOs/HistoricalReturnDtos.cs *(implemented as YearPerformanceDto in 002)*
- [x] T160 [US7] Create GetHistoricalReturnsUseCase in backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetHistoricalReturnsUseCase.cs *(implemented as HistoricalPerformanceService in 002)*
- [x] T161 [US7] Add historical returns endpoint to PortfoliosController in backend/src/InvestmentTracker.API/Controllers/PortfoliosController.cs *(implemented as PerformanceController in 002)*
- [x] T162 [US7] Implement year-end share calculation logic (shares held at specific date) in GetHistoricalReturnsUseCase *(implemented in HistoricalPerformanceService in 002)*
- [x] T163 [US7] Implement YTD return calculation in GetHistoricalReturnsUseCase *(implemented in HistoricalPerformanceService in 002)*

### Frontend - CAPE Service (C3)

- [x] T164 [P] [US7] Create CapeData types in frontend/src/types/index.ts
- [x] T165 [P] [US7] Create capeApi service with 24hr localStorage cache in frontend/src/services/capeApi.ts

### Frontend - Historical Returns Service

- [x] T166 [P] [US7] Add HistoricalReturn types in frontend/src/types/index.ts *(implemented as YearPerformance types in 002)*
- [x] T167 [P] [US7] Add getHistoricalReturns API method in frontend/src/services/api.ts *(implemented as calculateYearPerformance in 002)*

### Frontend - Dashboard Components (C4)

- [x] T168 [P] [US7] Create MarketContext component (CAPE display) in frontend/src/components/dashboard/MarketContext.tsx
- [x] T169 [P] [US7] Create HistoricalReturnsTable component in frontend/src/components/dashboard/HistoricalReturnsTable.tsx *(implemented as Performance.tsx page with PerformanceBarChart in 002)*
- [x] T170 [P] [US7] Create PositionAllocation component (with weights) in frontend/src/components/dashboard/PositionAllocation.tsx *(implemented as AssetAllocationPieChart in 002)*
- [x] T171 [US7] Update Dashboard page to integrate new components in frontend/src/pages/Dashboard.tsx
- [x] T172 [US7] Add useCapeData hook for CAPE fetching in frontend/src/hooks/useCapeData.ts
- [x] T173 [US7] Add useHistoricalReturns hook for returns fetching in frontend/src/hooks/useHistoricalReturns.ts *(implemented as useHistoricalPerformance in 002)*

### Polish & Edge Cases

- [x] T174 [US7] Add error handling for CAPE API failures (graceful degradation) in MarketContext component
- [x] T175 [US7] Add loading states for historical returns calculation *(implemented in Performance.tsx in 002)*
- [x] T176 [US7] Handle edge case: no historical data available (first year of investing) *(implemented in Performance.tsx in 002)*
- [x] T177 [US7] Handle edge case: missing year-end price (use last available trading day) *(implemented via MissingPriceModal in 002)*
- [x] T178 [US7] Add tooltip explanations for CAPE and return calculations *(implemented in Performance.tsx in 002)*

**Checkpoint**: Phase 12 complete - Dashboard displays CAPE, historical returns, and allocation weights

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
- **Phase 10 (Real-Time Data)**: Optional enhancement
- **Phase 11 (UI Enhancement)**: Depends on Phase 9
- **Phase 12 (Dashboard Analytics/US7)**: Depends on Phase 4 (needs portfolio performance foundation)

### User Story Dependencies

```
US1 (Stock Purchase) ─────┐
                          ├─→ US3 (Integration)
US2 (Currency Ledger) ────┘

US1 ──→ US4 (Performance) ──→ US5 (Sell/Realized PnL)
                          └──→ US7 (Dashboard Analytics)

US6 (Multi-Tenancy) ── runs parallel, integrates with all
```

### Phase 12 Parallel Opportunities

```bash
# Setup (sequential - schema changes)
T148 → T149 → T150 → T151

# Backend Historical Price Service (parallel interfaces)
T152 + T153 (parallel)
→ T154 → T155 + T156 (parallel) → T157 → T158

# Backend Historical Returns (sequential)
T159 → T160 → T161 → T162 → T163

# Frontend Services (all parallel)
T164 + T165 + T166 + T167 (parallel)

# Frontend Components (parallel, then integration)
T168 + T169 + T170 (parallel)
→ T171 → T172 + T173 (parallel)

# Polish (sequential after integration)
T174 → T175 → T176 → T177 → T178
```

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

---

## Phase 13: User Story 8 - Page Refresh Behavior (Priority: P8) ✅ COMPLETE

**Goal**: Auto-trigger quote fetch on page load/refresh, display cached values immediately to prevent flickering

**Independent Test**: Refresh Portfolio page with F5 → cached values appear immediately → loading spinner shows → fresh data replaces cached after fetch

**Requirements**: FR-040, FR-040a, FR-040b (Added 2026-01-09)

**Note**: This phase was already fully implemented. Cache logic is embedded in pages rather than in a separate hook.

### Implementation for User Story 8

- [x] T179 [US8] Quote cache logic embedded in loadCachedQuote/loadCachedPrices functions in Portfolio.tsx, Dashboard.tsx, PositionDetail.tsx
- [x] T180 [US8] Auto-fetch on mount implemented in Portfolio.tsx:103-140 (loadData with cached prices)
- [x] T181 [US8] Auto-fetch on mount implemented in Dashboard.tsx:74-126 (loadDashboardData with cached prices)
- [x] T182 [US8] PositionDetail.tsx:68-76 initializes state from cache, 118-150 auto-fetches on mount
- [x] T183 [US8] Loading state implemented in Portfolio.tsx:369-379 (isFetchingAll) and PositionCard.tsx:152 (fetchStatus)
- [x] T184 [US8] PositionCard.tsx:70-76 loads cached quote before render
- [x] T185 [US8] Cache-first display prevents flickering - all pages show cached values immediately

**Checkpoint**: Page refresh shows cached values instantly, then updates with fresh data

---

## Phase 14: User Story 9 - CSV Export (Priority: P9) ✅ COMPLETE

**Goal**: Export transactions and positions to CSV with UTF-8 BOM for Excel compatibility

**Independent Test**: Click export button → CSV file downloads → opens correctly in Excel with Chinese headers

**Requirements**: FR-041, FR-041a, FR-041b, FR-041c (Added 2026-01-09)

### Implementation for User Story 9

- [x] T186 [P] [US9] Create CSV export service in frontend/src/services/csvExport.ts
- [x] T187 [P] [US9] TransactionExportDto - using existing StockTransaction type directly
- [x] T188 [P] [US9] PositionExportDto - using existing StockPosition type directly
- [x] T189 [US9] Implement generateTransactionsCsv function in frontend/src/services/csvExport.ts
- [x] T190 [US9] Implement generatePositionsCsv function in frontend/src/services/csvExport.ts
- [x] T191 [US9] Implement downloadCsv function with UTF-8 BOM in frontend/src/services/csvExport.ts
- [x] T192 [US9] Add "匯出交易" button to Portfolio page in frontend/src/pages/Portfolio.tsx
- [x] T193 [US9] Add "匯出持倉" button to Portfolio page in frontend/src/pages/Portfolio.tsx
- [x] T194 [US9] Wire up export buttons to CSV service in frontend/src/pages/Portfolio.tsx

**Checkpoint**: Both transaction and position CSV exports work with correct Chinese headers in Excel

---

## Phase 15: User Story 10 - Market YTD Comparison (Priority: P10) ✅ COMPLETE

**Goal**: Display YTD returns for benchmark ETFs (VWRA, VUAA, 0050, VFEM) on Dashboard

**Independent Test**: Open Dashboard → see Market YTD section → shows YTD % for each benchmark

**Requirements**: FR-042, FR-042a, FR-042b, FR-042c (Added 2026-01-09)

### Backend Implementation (US10)

- [x] T195 [P] [US10] Create MarketYtdReturnDto in backend/src/InvestmentTracker.Application/DTOs/StockPriceDtos.cs
- [x] T196 [P] [US10] Create MarketYtdComparisonDto in backend/src/InvestmentTracker.Application/DTOs/StockPriceDtos.cs
- [x] T197 [US10] Create IMarketYtdService interface in backend/src/InvestmentTracker.Application/Interfaces/IMarketYtdService.cs
- [x] T198 [US10] Implement MarketYtdService in backend/src/InvestmentTracker.Infrastructure/Services/MarketYtdService.cs
- [x] T199 [US10] Add GetJan1BenchmarkPrice method using IndexPriceSnapshot with YYYY01 format
- [x] T200 [US10] Add GetCurrentBenchmarkPrice method using IStockPriceService
- [x] T201 [US10] Add CalculateYtdReturn method: ((Current - Jan1) / Jan1) × 100
- [x] T202 [US10] Add YTD endpoints to MarketDataController (ytd-comparison, ytd-jan1-price, ytd-benchmarks)
- [x] T203 [US10] Implement GET /api/market-data/ytd-comparison endpoint
- [x] T204 [US10] Register IMarketYtdService in DI container in backend/src/InvestmentTracker.Api/Program.cs

### Frontend Implementation (US10)

- [x] T205 [P] [US10] Create MarketYtdReturn type in frontend/src/types/index.ts
- [x] T206 [P] [US10] Create MarketYtdComparison type in frontend/src/types/index.ts
- [x] T207 [US10] Add getYtdComparison API client in frontend/src/services/api.ts
- [x] T208 [US10] Create MarketYtdSection component in frontend/src/components/dashboard/MarketYtdSection.tsx
- [x] T209 [US10] Display benchmark ETF names and YTD percentages in MarketYtdSection
- [x] T210 [US10] Add visual styling (green/red for positive/negative) in MarketYtdSection
- [x] T211 [US10] Integrate MarketYtdSection into Dashboard page in frontend/src/pages/Dashboard.tsx
- [x] T212 [US10] Add loading and error states to MarketYtdSection

**Checkpoint**: Dashboard shows YTD returns for VWRA, VUAA, 0050, VFEM with correct calculations

---

## Phase 16: Taiwan Stock Support Enhancement (Priority: P11) ✅ COMPLETE

**Goal**: Ensure Taiwan stocks (TWD source currency) work correctly with exchange rate = 1.0

**Independent Test**: Add Taiwan stock transaction → verify exchange rate = 1.0, totals correct

**Requirements**: FR-043, FR-043a, FR-043b (Added 2026-01-09)

- [x] T213 [P] [US11] Taiwan stock detection using isTaiwanStock() in TransactionForm (pattern: /^\d+[A-Za-z]*$/)
- [x] T214 [US11] Auto-set exchange rate to 1.0 for Taiwan stocks in TransactionForm (handleChange + handleTickerBlur)
- [x] T215 [US11] Visual indicator for Taiwan market already exists in PositionCard (MARKET_LABELS with 台股 tag)
- [x] T216 [US11] TWSE and TPEx quotes working via TwseStockPriceProvider.cs

**Checkpoint**: Taiwan stock transactions work correctly with TWD/TWD = 1.0 exchange rate

---

## Updated Dependencies & Execution Order

### New Phase Dependencies

- **Phase 13 (US8 - Page Refresh)**: Can start immediately - frontend only, no backend changes
- **Phase 14 (US9 - CSV Export)**: Can start immediately - frontend only, no backend changes
- **Phase 15 (US10 - Market YTD)**: Requires backend + frontend work, depends on Phase 12 patterns
- **Phase 16 (US11 - Taiwan Stock)**: Can start immediately - mostly frontend validation

### Parallel Execution Opportunities

```bash
# All three new phases can start in parallel:
# Developer A: Phase 13 (US8 Page Refresh) - T179-T185
# Developer B: Phase 14 (US9 CSV Export) - T186-T194
# Developer C: Phase 15 Backend (US10) - T195-T204
# Developer D: Phase 16 (US11 Taiwan) - T213-T216

# After US10 Backend completes:
# Developer C continues: Phase 15 Frontend - T205-T212
```

---

## Implementation Strategy for New Features

### Suggested Priority Order (Single Developer)

1. **Phase 13 (US8)** first - Quickest, highest UX impact (fixes flickering)
2. **Phase 14 (US9)** second - Frontend-only, immediate user value
3. **Phase 16 (US11)** third - Quick validation fix for Taiwan stocks
4. **Phase 15 (US10)** fourth - Requires backend work, more complex

### Task Count Summary

| Phase | Story | Task Count | Status |
|-------|-------|------------|--------|
| Phase 13 | US8 (Page Refresh) | 7 tasks | 🆕 Pending |
| Phase 14 | US9 (CSV Export) | 9 tasks | 🆕 Pending |
| Phase 15 | US10 (Market YTD) | 18 tasks | 🆕 Pending |
| Phase 16 | US11 (Taiwan Stock) | 4 tasks | ✅ Complete |
| **Total New** | | **38 tasks** | |

---

## Phase 17: Currency Ledger UI Redesign (Priority: P12) ✅ COMPLETE

**Goal**: Redesign Currency Ledger UI to focus on "holding currency for investment" use case, not currency trading

**Independent Test**:
1. Navigate to `/currency` → Only 淨投入 shown in summary (no 成本/損益)
2. View ledger card → Shows "USD @ 32.15" format, balance with TWD equivalent, no 成本/損益
3. Navigate to `/currency/:id` → Rate in header, no name editing, only Balance/AvgRate/NetInvestment metrics

**Spec Reference**: Session 2026-01-09 (Currency Ledger Redesign) in spec.md

### Implementation for Currency Overview Page

- [x] T217 [US12] Remove `totalCost` calculation and display from summary in `frontend/src/pages/Currency.tsx`
- [x] T218 [US12] Remove `totalRealizedPnl` calculation and display from summary in `frontend/src/pages/Currency.tsx`
- [x] T219 [US12] Remove `totalInterest` from summary section in `frontend/src/pages/Currency.tsx`
- [x] T220 [US12] Keep only 淨投入 (`totalExchanged`) as the summary metric in `frontend/src/pages/Currency.tsx`

### Implementation for Currency Ledger Card Redesign

- [x] T221 [P] [US12] Add real-time exchange rate fetching state to CurrencyLedgerCard in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T222 [P] [US12] Add stockPriceApi.getExchangeRate call on component mount in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T223 [US12] Restructure header to display "USD @ rate" format in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T224 [US12] Add TWD equivalent display (balance × current rate) below balance in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T225 [US12] Remove 目前成本 (`totalCost`) display section in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T226 [US12] Remove 已實現損益 (`realizedPnl`) display section in `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- [x] T227 [US12] Ensure 利息收入 only shows when `totalInterest > 0` in `frontend/src/components/currency/CurrencyLedgerCard.tsx`

### Implementation for Currency Detail Page Updates

- [x] T228 [P] [US12] Move exchange rate display to header next to currency code in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T229 [P] [US12] Remove name editing state variables (`isEditingName`, `editName`) in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T230 [P] [US12] Remove `handleStartEditName` and `handleSaveName` functions in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T231 [US12] Remove name editing UI (pencil icon, input field, save/cancel buttons) in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T232 [US12] Remove "目前成本" metric card from grid in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T233 [US12] Remove "已實現損益" metric card from grid in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T234 [US12] Remove "即時匯率" metric card (rate is now in header) in `frontend/src/pages/CurrencyDetail.tsx`
- [x] T235 [US12] Reorganize remaining metric cards (Balance, Average Rate, Net Investment) in `frontend/src/pages/CurrencyDetail.tsx`

### Validation

- [x] T236 [US12] Run `npm run build` to verify no TypeScript errors in `frontend/`
- [x] T237 [US12] Manual test: Navigate through Currency pages and verify all changes match spec *(validated with v1.0.0 release)*

**Checkpoint**: Currency Ledger UI simplified - metrics focused on investment holding use case

---

## Phase 17 Parallel Opportunities

```bash
# All three file groups can be modified in parallel (different files):

# Group A: Currency.tsx (T217-T220) - Overview page summary
# Group B: CurrencyLedgerCard.tsx (T221-T227) - Card redesign
# Group C: CurrencyDetail.tsx (T228-T235) - Detail page updates

# Within Group B and C, [P] marked tasks can run in parallel
```

---

## Updated Task Count Summary

| Phase | Story | Task Count | Status |
|-------|-------|------------|--------|
| Phase 1-12 | US1-US7 | 178 tasks | ✅ Complete |
| Phase 13 | US8 (Page Refresh) | 7 tasks | ✅ Complete |
| Phase 14 | US9 (CSV Export) | 9 tasks | ✅ Complete |
| Phase 15 | US10 (Market YTD) | 18 tasks | ✅ Complete |
| Phase 16 | US11 (Taiwan Stock) | 4 tasks | ✅ Complete |
| Phase 17 | US12 (Currency UI Redesign) | 21 tasks | ✅ Complete |
| Phase 18 | US13 (Stock Split Handling) | 17 tasks | ✅ Complete |
| **Total** | | **254 tasks** | |

---

## Phase 18: Stock Split Handling (Priority: P13)

**Goal**: Track stock split events and automatically adjust historical transaction values at display time

**Independent Test**: Add 0050 split record (1:4 on 2025-06-18) → View transactions before split date → Verify adjusted shares = original × 4, adjusted price = original / 4

**Spec Reference**: plan.md §Recent Updates (2026-01-10), data-model.md §8, research.md §18

### Tests for User Story 13

- [x] T251 [P] [US13] Create StockSplitAdjustmentService unit tests (including cumulative splits) in `backend/tests/InvestmentTracker.Domain.Tests/Services/StockSplitAdjustmentServiceTests.cs`

### Domain Layer (US13)

- [x] T238 [P] [US13] Create StockMarket enum (TW, US, UK) in `backend/src/InvestmentTracker.Domain/Enums/StockMarket.cs`
- [x] T239 [US13] Create StockSplit entity in `backend/src/InvestmentTracker.Domain/Entities/StockSplit.cs`
- [x] T240 [US13] Create StockSplitAdjustmentService (GetAdjustedShares, GetAdjustedPrice) in `backend/src/InvestmentTracker.Domain/Services/StockSplitAdjustmentService.cs`

### Infrastructure Layer (US13)

- [x] T241 [US13] Create StockSplitConfiguration in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/StockSplitConfiguration.cs`
- [x] T242 [US13] Add DbSet<StockSplit> to AppDbContext in `backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs`
- [x] T243 [US13] Create and apply EF Core migration for stock_splits table
- [x] T244 [US13] Create IStockSplitRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/IStockSplitRepository.cs`
- [x] T245 [US13] Implement StockSplitRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/StockSplitRepository.cs`

### Application Layer (US13)

- [x] T246 [P] [US13] Create StockSplitDto in `backend/src/InvestmentTracker.Application/DTOs/StockSplitDtos.cs`
- [x] T247 [US13] Create GetStockSplitsUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplits/GetStockSplitsUseCase.cs`
- [x] T248 [US13] Create CreateStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplits/CreateStockSplitUseCase.cs`
- [x] T252 [US13] Create UpdateStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplits/UpdateStockSplitUseCase.cs`
- [x] T253 [US13] Create DeleteStockSplitUseCase in `backend/src/InvestmentTracker.Application/UseCases/StockSplits/DeleteStockSplitUseCase.cs`

### API Layer (US13)

- [x] T249 [US13] Create StockSplitsController (GET, POST, PUT, DELETE) in `backend/src/InvestmentTracker.API/Controllers/StockSplitsController.cs`

### Integration (US13)

- [x] T250 [US13] Update GetPortfolioSummaryUseCase to apply split adjustments when returning positions in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetPortfolioSummaryUseCase.cs`

### Frontend (US13)

- [x] T254 [US13] Update TransactionList to display original and adjusted values (shares, price) for split-affected transactions in `frontend/src/components/transactions/TransactionList.tsx`

**Checkpoint**: Stock split records can be created/updated/deleted, portfolio displays adjusted shares/prices, and transaction list shows both original and adjusted values

---

## Phase 18 Parallel Opportunities

```bash
# Tests (parallel with Domain setup)
T251 (unit tests for adjustment service)

# Domain (T238 parallel, then sequential)
T238 (StockMarket enum)
→ T239 (StockSplit entity) → T240 (AdjustmentService)

# Infrastructure (sequential - schema changes)
T241 → T242 → T243 → T244 → T245

# Application (T246 parallel, then sequential)
T246 (DTO) + T247 + T248 + T252 + T253 (parallel after T245)

# API + Integration + Frontend (sequential)
T249 → T250 → T254
```
