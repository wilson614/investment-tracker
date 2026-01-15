# Tasks: Portfolio Enhancements V2

**Input**: Design documents from `/specs/002-portfolio-enhancements/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md
**Base Module**: Extends 001-portfolio-tracker

**Tests**: Not explicitly requested - test tasks omitted. Add manually if TDD approach needed.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US6)
- Paths use web app structure: `backend/src/`, `frontend/src/`

---

## Phase 1: Setup (Database Migrations)

**Purpose**: Database schema changes required for new features

- [ ] T001 Create EF Core migration for nullable ExchangeRate in `backend/src/InvestmentTracker.Infrastructure/Migrations/`
- [ ] T002 [P] Create EuronextQuoteCache entity in `backend/src/InvestmentTracker.Domain/Entities/EuronextQuoteCache.cs`
- [ ] T003 [P] Create EtfClassification entity in `backend/src/InvestmentTracker.Domain/Entities/EtfClassification.cs`
- [ ] T004 [P] Create EtfType enum in `backend/src/InvestmentTracker.Domain/Enums/EtfType.cs`
- [ ] T005 Add DbSet registrations for new entities in `backend/src/InvestmentTracker.Infrastructure/Data/ApplicationDbContext.cs`
- [ ] T006 Create EF Core migration for new entities in `backend/src/InvestmentTracker.Infrastructure/Migrations/`
- [ ] T007 Run migrations and verify database schema

**Checkpoint**: Database ready for feature implementation

---

## Phase 2: Foundational (Shared Services)

**Purpose**: Core infrastructure that supports multiple user stories

- [ ] T008 Create IEuronextApiClient interface in `backend/src/InvestmentTracker.Domain/Interfaces/IEuronextApiClient.cs`
- [ ] T009 Implement EuronextApiClient in `backend/src/InvestmentTracker.Infrastructure/External/EuronextApiClient.cs`
- [ ] T010 [P] Create EuronextQuoteCacheRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/EuronextQuoteCacheRepository.cs`
- [ ] T011 [P] Create EtfClassificationRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/EtfClassificationRepository.cs`
- [ ] T012 Register new services in DI container in `backend/src/InvestmentTracker.API/Program.cs`

**Checkpoint**: Foundation ready - user story implementation can begin

---

## Phase 3: User Story 1 - Optional Exchange Rate (Priority: P1) 🎯 MVP

**Goal**: Allow stock transactions without exchange rate, display costs in native currency

**Independent Test**: Add USD transaction without exchange rate → verify cost displays as USD

### Backend Implementation

- [ ] T013 [US1] Modify StockTransaction entity to make ExchangeRate nullable in `backend/src/InvestmentTracker.Domain/Entities/StockTransaction.cs`
- [ ] T014 [US1] Update CreateStockTransactionRequest DTO to allow null ExchangeRate in `backend/src/InvestmentTracker.API/DTOs/StockTransactionDtos.cs`
- [ ] T015 [US1] Update StockTransactionService to handle null ExchangeRate in `backend/src/InvestmentTracker.Application/Services/StockTransactionService.cs`
- [ ] T016 [US1] Update PortfolioService to group holdings by (Symbol, Currency, HasExchangeRate) in `backend/src/InvestmentTracker.Application/Services/PortfolioService.cs`
- [ ] T017 [US1] Update XIRR calculation to handle mixed currency transactions in `backend/src/InvestmentTracker.Domain/Services/XirrCalculator.cs`
- [ ] T018 [US1] Update GetHoldingsResponse DTO to include currency display info in `backend/src/InvestmentTracker.API/DTOs/PortfolioDtos.cs`

### Frontend Implementation

- [ ] T019 [US1] Update StockTransactionForm to make exchange rate optional in `frontend/src/components/forms/StockTransactionForm.tsx`
- [ ] T020 [US1] Update Holdings component to display costs in source currency when no exchange rate in `frontend/src/components/Holdings.tsx`
- [ ] T021 [US1] Update TypeScript types for nullable exchange rate in `frontend/src/types/transaction.ts`

**Checkpoint**: US1 complete - transactions without exchange rate work correctly

---

## Phase 4: User Story 2 - Dashboard Pie Chart (Priority: P2)

**Goal**: Display asset allocation as pie chart on dashboard

**Independent Test**: Login to dashboard → verify pie chart renders with correct percentages

### Backend Implementation

- [ ] T022 [US2] Create GetAssetAllocationResponse DTO in `backend/src/InvestmentTracker.API/DTOs/DashboardDtos.cs`
- [ ] T023 [US2] Add GetAssetAllocation endpoint to DashboardController in `backend/src/InvestmentTracker.API/Controllers/DashboardController.cs`
- [ ] T024 [US2] Implement asset allocation calculation in PortfolioService in `backend/src/InvestmentTracker.Application/Services/PortfolioService.cs`

### Frontend Implementation

- [ ] T025 [P] [US2] Create AssetAllocationPieChart component in `frontend/src/components/charts/AssetAllocationPieChart.tsx`
- [ ] T026 [US2] Add useAssetAllocation hook in `frontend/src/hooks/useAssetAllocation.ts`
- [ ] T027 [US2] Integrate pie chart into Dashboard page in `frontend/src/pages/Dashboard.tsx`
- [ ] T028 [US2] Add chart color constants in `frontend/src/constants/chartColors.ts`

**Checkpoint**: US2 complete - pie chart displays asset allocation on dashboard

---

## Phase 5: User Story 3 - Euronext Exchange Support (Priority: P3)

**Goal**: Support Euronext-listed stocks with real-time quotes

**Independent Test**: Add AGAC (IE000FHBZDZ8) → fetch quote → verify price displays

### Backend Implementation

- [ ] T029 [US3] Create EuronextQuoteService in `backend/src/InvestmentTracker.Application/Services/EuronextQuoteService.cs`
- [ ] T030 [US3] Add Euronext quote endpoint to MarketDataController in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [ ] T031 [US3] Extend IQuoteProvider to support Euronext market in `backend/src/InvestmentTracker.Domain/Interfaces/IQuoteProvider.cs`
- [ ] T032 [US3] Update HistoricalPrice repository to support Euronext MIC codes in `backend/src/InvestmentTracker.Infrastructure/Repositories/HistoricalPriceRepository.cs`

### Frontend Implementation

- [X] T033 [US3] Add Euronext symbol mapping constants in `frontend/src/constants/euronextSymbols.ts`
- [X] T034 [US3] Update quote fetching service to support Euronext in `frontend/src/services/api.ts`
- [X] T035 [US3] Add stale quote indicator component in `frontend/src/components/common/StaleQuoteIndicator.tsx`
- [X] T036 [US3] Integrate stale indicator into PositionCard component in `frontend/src/components/portfolio/PositionCard.tsx`

**Checkpoint**: US3 complete - Euronext stocks display with real-time quotes

---

## Phase 6: User Story 4 - Historical Year Performance (Priority: P4)

**Goal**: View performance for any historical year (2020+)

**Independent Test**: Select 2024 from year dropdown → verify XIRR and return display

### Backend Implementation

- [X] T037 [US4] Create HistoricalPerformanceService in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [X] T038 [US4] Add year performance endpoint to PerformanceController in `backend/src/InvestmentTracker.API/Controllers/PerformanceController.cs`
- [X] T039 [US4] Add available years endpoint to PerformanceController in `backend/src/InvestmentTracker.API/Controllers/PerformanceController.cs`
- [X] T040 [US4] Create YearPerformanceResponse DTO in `backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs`
- [X] T041 [US4] Add missing price detection and prompt logic in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`

### Frontend Implementation

- [X] T042 [P] [US4] Create YearSelector component in `frontend/src/components/performance/YearSelector.tsx`
- [X] T043 [US4] Add useHistoricalPerformance hook in `frontend/src/hooks/useHistoricalPerformance.ts`
- [X] T044 [US4] Integrate year selector into Performance page in `frontend/src/pages/Performance.tsx`
- [X] T045 [US4] Add missing price input modal in `frontend/src/components/modals/MissingPriceModal.tsx`

**Checkpoint**: US4 complete - historical year performance displays correctly

---

## Phase 7: User Story 5 - Extended YTD Support (Priority: P5)

**Goal**: YTD calculation for all stock types with dividend adjustment for Taiwan stocks

**Independent Test**: View YTD for portfolio with Taiwan stocks → verify dividend adjustment applied

### Backend Implementation

- [X] T046 [US5] Create EtfClassificationService in `backend/src/InvestmentTracker.Application/Services/EtfClassificationService.cs`
- [X] T047 [US5] Add ETF classification endpoints to controller in `backend/src/InvestmentTracker.API/Controllers/EtfClassificationController.cs`
- [X] T048 [US5] Update YTD calculation to apply dividend adjustment for Taiwan stocks in `backend/src/InvestmentTracker.Infrastructure/Services/MarketYtdService.cs`
- [X] T049 [US5] Extend YTD support to all accumulating ETFs (via EtfClassificationService.NeedsDividendAdjustment) in `backend/src/InvestmentTracker.Infrastructure/Services/MarketYtdService.cs`
- [X] T050 [US5] Add ETF type detection with "Unknown" default in `backend/src/InvestmentTracker.Application/Services/EtfClassificationService.cs`

### Frontend Implementation

- [X] T051 [P] [US5] Create EtfTypeBadge component for "Unconfirmed type" indicator in `frontend/src/components/common/EtfTypeBadge.tsx`
- [X] T052 [US5] Add ETF classification management UI in `frontend/src/services/api.ts`
- [X] T053 [US5] Integrate ETF type badge into PositionCard component in `frontend/src/components/portfolio/PositionCard.tsx`
- [X] T054 [US5] Update YTD display to show dividend-adjusted returns in `frontend/src/pages/Performance.tsx`

**Checkpoint**: US5 complete - ETF classification service and badges integrated

---

## Phase 8: User Story 6 - Performance Bar Charts (Priority: P6)

**Goal**: Display performance comparison using bar charts

**Independent Test**: Navigate to performance comparison → verify bar chart renders with correct data

### Frontend Implementation

- [X] T055 [P] [US6] Create PerformanceBarChart component in `frontend/src/components/charts/PerformanceBarChart.tsx`
- [X] T056 [US6] Update Performance page to use bar chart for comparison in `frontend/src/pages/Performance.tsx`
- [X] T057 [US6] Add color coding for positive/negative returns in `frontend/src/components/charts/PerformanceBarChart.tsx`
- [X] T058 [US6] Add hover tooltip with detailed performance data in `frontend/src/components/charts/PerformanceBarChart.tsx`

**Checkpoint**: US6 complete - performance comparison displays as bar chart

---

## Phase 9: User Story 7 - Auto-Fetch Price for New Positions (Priority: P1) *(NEW)*

**Goal**: Automatically fetch real-time stock price when adding a transaction for a new stock

**Independent Test**: Add a Buy transaction for a stock not currently held → price should auto-fetch within 3 seconds

### Frontend Implementation

- [X] T065 [US7] Modify handleAddTransaction to detect new ticker after save in `frontend/src/pages/Portfolio.tsx`
- [X] T066 [US7] Add auto-fetch logic for new position price using existing stockPriceApi/marketDataApi in `frontend/src/pages/Portfolio.tsx`
- [X] T067 [US7] Ensure PositionCard displays fetched price immediately in `frontend/src/components/portfolio/PositionCard.tsx`

**Checkpoint**: US7 complete - new positions display current price within 3 seconds

---

## Phase 10: User Story 8 - Euronext Change Percentage Display (Priority: P2) *(NEW)*

**Goal**: Display "Since Previous Close" percentage change for Euronext stocks

**Independent Test**: View a Euronext stock position (e.g., AGAC) → should display change percentage with color coding

### Backend Implementation

- [X] T068 [US8] Add regex pattern to extract change percentage from Euronext HTML in `backend/src/InvestmentTracker.Infrastructure/External/EuronextApiClient.cs`
- [X] T069 [US8] Update EuronextQuoteResult record to include ChangePercent and Change properties in `backend/src/InvestmentTracker.Infrastructure/External/EuronextApiClient.cs`
- [X] T070 [US8] Update EuronextQuoteService to persist and return change fields in `backend/src/InvestmentTracker.Infrastructure/Services/EuronextQuoteService.cs`
- [X] T071 [US8] Add ChangePercent and Change columns to EuronextQuoteCache entity in `backend/src/InvestmentTracker.Domain/Entities/EuronextQuoteCache.cs`
- [X] T072 [US8] Create EF migration for EuronextQuoteCache new columns in `backend/src/InvestmentTracker.Infrastructure/`

### Frontend Implementation

- [X] T073 [US8] Update frontend EuronextQuoteResponse type to include changePercent in `frontend/src/types/index.ts`
- [X] T074 [US8] Display change percentage with color coding in PositionCard in `frontend/src/components/portfolio/PositionCard.tsx`

**Checkpoint**: US8 complete - Euronext stocks display change percentage (green/red)

---

## Phase 11: User Story 9 - Foreign Currency Portfolio (Priority: P1) *(NEW)*

**Goal**: Create dedicated portfolio type for foreign currency stocks without exchange rate requirements

**Independent Test**: Create Foreign Currency Portfolio with USD base currency → add USD stock → all metrics display in USD

### Backend Implementation

- [X] T075 [US9] Create PortfolioType enum in `backend/src/InvestmentTracker.Domain/Enums/PortfolioType.cs`
- [X] T076 [US9] Add PortfolioType and DisplayName properties to Portfolio entity in `backend/src/InvestmentTracker.Domain/Entities/Portfolio.cs`
- [X] T077 [US9] Create EF migration for Portfolio new columns in `backend/src/InvestmentTracker.Infrastructure/`
- [X] T078 [US9] Update PortfolioController to handle portfolioType in create/list endpoints in `backend/src/InvestmentTracker.API/Controllers/PortfolioController.cs`
- [X] T079 [US9] ~~Update CreatePortfolioUseCase~~ Skipped - validation handled by Portfolio entity constructor
- [X] T080 [US9] Update GetPortfolioSummaryUseCase to calculate metrics in source currency for FC portfolios in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/GetPortfolioSummaryUseCase.cs`
- [X] T081 [US9] Modify XIRR calculation to use source currency cash flows for FC portfolios in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/CalculateXirrUseCase.cs`
- [X] T082 [US9] ~~Add stock currency validation~~ Skipped - FC portfolios don't require currency validation

### Frontend Implementation

- [X] T083 [P] [US9] Add PortfolioType type to frontend types in `frontend/src/types/index.ts`
- [X] T084 [P] [US9] Create PortfolioSelector component for portfolio switching in `frontend/src/components/portfolio/PortfolioSelector.tsx`
- [X] T085 [US9] Add portfolio creation form with portfolioType selection in `frontend/src/components/portfolio/CreatePortfolioForm.tsx`
- [X] T086 [US9] Integrate PortfolioSelector into Portfolio page in `frontend/src/pages/Portfolio.tsx`
- [X] T087 [US9] Update TransactionForm to hide exchange rate for FC portfolios in `frontend/src/components/transactions/TransactionForm.tsx`
- [X] T088 [US9] ~~Update PositionCard~~ Skipped - already works correctly (backend returns source currency in "Home" fields)
- [X] T089 [US9] ~~Update api.ts~~ Already implemented in previous session

**Checkpoint**: US9 complete - Foreign Currency Portfolio displays all metrics in source currency

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Final improvements across all features

- [X] T059 [P] Update API documentation in Swagger for new endpoints
- [X] T060 [P] Add error handling for Euronext API failures with user-friendly messages
- [X] T061 Run quickstart.md validation scenarios
- [X] T062 Verify all user stories work independently
- [X] T063 Performance testing for chart rendering (<2 seconds target)
- [X] T064 Code cleanup and remove any unused imports

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies - database migrations first
- **Phase 2 (Foundational)**: Depends on Phase 1 - shared services
- **Phase 3-8 (User Stories)**: All depend on Phase 2 completion
  - US1 → US2 → US3 → US4 → US5 → US6 (sequential by priority)
  - OR: All stories can proceed in parallel after Phase 2
- **Phase 9 (Polish)**: After desired user stories complete

### User Story Independence

| Story | Dependencies | Can Run After |
|-------|--------------|---------------|
| US1 (P1) | Phase 2 only | Phase 2 complete |
| US2 (P2) | Phase 2 only | Phase 2 complete |
| US3 (P3) | Phase 2 only | Phase 2 complete |
| US4 (P4) | Phase 2 only | Phase 2 complete |
| US5 (P5) | Phase 2 only | Phase 2 complete |
| US6 (P6) | Phase 2 only | Phase 2 complete |
| US7 (P1) *(NEW)* | Phase 2 only | Phase 2 complete |
| US8 (P2) *(NEW)* | US3 (Euronext infra) | US3 complete |
| US9 (P1) *(NEW)* | Phase 2 only | Phase 2 complete |

All user stories are independent and can be implemented in parallel (except US8 depends on US3 Euronext infrastructure).

### Parallel Opportunities

```text
Phase 1: T002, T003, T004 can run in parallel
Phase 2: T010, T011 can run in parallel
US2: T025 can run in parallel with backend tasks
US4: T042 can run in parallel with backend tasks
US5: T051 can run in parallel with backend tasks
US6: T055 can run in parallel
US9: T083, T084 can run in parallel with backend tasks (NEW)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T007)
2. Complete Phase 2: Foundational (T008-T012)
3. Complete Phase 3: User Story 1 (T013-T021)
4. **STOP and VALIDATE**: Test optional exchange rate feature
5. Deploy if ready - MVP complete!

### Incremental Delivery

| Increment | Stories | Value Delivered |
|-----------|---------|-----------------|
| MVP | US1 | Optional exchange rate transactions |
| +1 | US2 | Dashboard pie chart visualization |
| +2 | US3 | Euronext exchange support |
| +3 | US4 | Historical year performance |
| +4 | US5 | Extended YTD with dividend adjustment |
| +5 | US6 | Bar chart performance visualization |
| +6 | US7 | Auto-fetch price for new positions *(NEW)* |
| +7 | US8 | Euronext change percentage display *(NEW)* |
| +8 | US9 | Foreign Currency Portfolio *(NEW)* |

---

## Summary

| Metric | Count |
|--------|-------|
| Total Tasks | 89 |
| Setup Tasks | 7 |
| Foundational Tasks | 5 |
| US1 Tasks | 9 |
| US2 Tasks | 7 |
| US3 Tasks | 8 |
| US4 Tasks | 9 |
| US5 Tasks | 9 |
| US6 Tasks | 4 |
| US7 Tasks *(NEW)* | 3 |
| US8 Tasks *(NEW)* | 7 |
| US9 Tasks *(NEW)* | 15 |
| Polish Tasks | 6 |
| Parallel Opportunities | 14 tasks marked [P] |
