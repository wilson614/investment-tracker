# Tasks: Portfolio Enhancements V2

**Input**: Design documents from `/specs/002-portfolio-enhancements/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md
**Base Module**: Extends 001-portfolio-tracker

**Tests**: Required for financial-calculation correctness (see constitution). Include targeted tests for XIRR + FX + benchmark caching.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US12)
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

## Phase 11: User Story 9 - Single Portfolio with Auto-Filled Historical Exchange Rates (Priority: P1) *(UPDATED)*

**Goal**: Keep a single portfolio model and allow transactions to omit ExchangeRate; TWD-based metrics MUST auto-fill missing FX using historical FX on the transaction date.

**Independent Test**: Create 1 USD transaction with ExchangeRate and 1 USD transaction without ExchangeRate → TWD XIRR still computes using transaction-date historical FX; if FX lookup fails, user is prompted for manual FX input.

### Backend Implementation

- [ ] T075 [US9] Remove/disable multi-portfolio currency mode (PortfolioType) usage paths and align to single-portfolio behavior in `backend/src/InvestmentTracker.Domain/Entities/Portfolio.cs`
- [ ] T076 [US9] Update TWD XIRR cashflow building to use FX auto-fill when ExchangeRate is null in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/CalculateXirrUseCase.cs`
- [ ] T077 [US9] Add transaction-date FX cache entity and persistence (if not already present) in `backend/src/InvestmentTracker.Domain/Entities/`
- [ ] T078 [US9] Implement transaction-date FX cache repository/service (cache → Stooq → persist) in `backend/src/InvestmentTracker.Infrastructure/Services/`
- [ ] T079 [US9] Add API endpoint to submit manual FX rate for a specific transaction date when lookup fails in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`

### Frontend Implementation

- [ ] T080 [US9] Ensure transaction form keeps ExchangeRate optional (nullable) in `frontend/src/pages/Portfolio.tsx`
- [ ] T081 [US9] Add UI prompt/modal for missing FX (transaction-date) and submit manual FX rate in `frontend/src/pages/Portfolio.tsx`
- [ ] T082 [US9] Add API client for manual FX submission in `frontend/src/services/api.ts`

### Tests (Required)

- [ ] T083 [US9] Add unit tests for TWD XIRR FX auto-fill (known edge cases) in `backend/tests/InvestmentTracker.API.Tests/`

**Checkpoint**: US9 complete - single portfolio works; missing ExchangeRate does not break TWD metrics

---

## Phase 12: User Story 10 - Performance Page UX Improvements + Benchmark Robustness (Priority: P2) *(UPDATED)*

**Goal**: Improve performance page labels, benchmarks, and loading states

**Independent Test**: View performance page → verify transaction count display, 11 benchmarks, no flicker on switch

### Backend Implementation (Benchmark negative caching)

- [ ] T090 [US10] Add NotAvailable marker support for `(MarketKey, YearMonth)` benchmark month-end fetches in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [ ] T091 [US10] Persist NotAvailable permanently when Stooq returns null in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [ ] T092 [US10] Skip Stooq calls when NotAvailable exists (must return null immediately) in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`

### Frontend Implementation (UX)

- [ ] T093 [US10] Update XIRR TWD card to show transaction count instead of cash flow count in `frontend/src/components/performance/XirrCard.tsx`
- [ ] T094 [US10] Replace explanatory text with info icon (ℹ️) tooltip for XIRR USD card in `frontend/src/components/performance/XirrCard.tsx`
- [ ] T095 [US10] Add dynamic label "目前價值"/"年底價值" based on year (YTD vs historical) in `frontend/src/components/performance/YearlySummary.tsx`
- [ ] T096 [US10] Update benchmark dropdown to display all 11 benchmarks in `frontend/src/components/dashboard/MarketYtdSection.tsx`
- [ ] T097 [P] [US10] Create BenchmarkSettings component with settings gear (⚙️) popup in `frontend/src/components/performance/BenchmarkSettings.tsx`
- [ ] T098 [US10] Implement multi-select for benchmark comparison in `frontend/src/components/performance/BenchmarkSettings.tsx`
- [ ] T099 [US10] Sync benchmark preferences with dashboard localStorage (`ytd_benchmark_preferences`) in `frontend/src/hooks/useBenchmarkPreferences.ts`
- [ ] T100 [US10] Enforce max 10 selected benchmarks in BenchmarkSettings (block >10) in `frontend/src/components/performance/BenchmarkSettings.tsx`
- [ ] T101 [US10] Implement render-once gate for current-year comparison (holdings + benchmarks ready) in `frontend/src/pages/Performance.tsx`
- [ ] T102 [US10] Prevent flicker by keeping previous benchmark series during loading in `frontend/src/components/charts/PerformanceBarChart.tsx`
- [ ] T103 [US10] Show "手動輸入" button only after auto-fetch fails (not immediately) in `frontend/src/pages/Performance.tsx`
- [ ] T104 [US10] Add skeleton loader for XIRR cards during calculation in `frontend/src/components/performance/XirrCard.tsx`

**Checkpoint**: US10 complete - performance page UX improved with better labels and loading states

---

## Phase 13: User Story 11 - Performance State Reset on Portfolio Data Changes (Priority: P1) *(UPDATED)*

**Goal**: Clear stale data when switching portfolios to prevent misleading displays

**Independent Test**: Switch from portfolio A to B → XIRR should show loading (not A's value)

### Frontend Implementation

- [ ] T105 [US11] Ensure query keys include `portfolioId` so performance state resets on portfolio changes in `frontend/src/hooks/useHistoricalPerformance.ts`
- [ ] T106 [US11] Clear derived UI state on portfolio changes (show loading/empty, not stale value) in `frontend/src/pages/Performance.tsx`
- [ ] T107 [US11] Ensure new empty portfolio shows "-" or loading for XIRR (not previous portfolio values) in `frontend/src/pages/Performance.tsx`
- [ ] T108 [US11] Add frontend test for performance state reset on portfolio change in `frontend/` (Jest/RTL)

**Checkpoint**: US11 complete - portfolio switching clears stale XIRR data within 100ms

---

## Phase 14: User Story 12 - Historical Year-End Price Cache (Priority: P1) *(UPDATED)*

**Goal**: Cache year-end stock prices and exchange rates to prevent API rate limit failures

**Independent Test**: Calculate 2024 performance for VT twice → second calculation should use cache (no API call)

### Backend Implementation

- [ ] T109 [P] [US12] Ensure HistoricalYearEndData entity exists in `backend/src/InvestmentTracker.Domain/Entities/HistoricalYearEndData.cs`
- [ ] T110 [P] [US12] Ensure HistoricalDataType enum exists in `backend/src/InvestmentTracker.Domain/Enums/HistoricalDataType.cs`
- [ ] T111 [US12] Ensure EF migration exists in `backend/src/InvestmentTracker.Infrastructure/Persistence/Migrations/`
- [ ] T112 [US12] Ensure DbSet/config exists in `backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs`
- [ ] T113 [P] [US12] Ensure repository interface exists in `backend/src/InvestmentTracker.Domain/Interfaces/IHistoricalYearEndDataRepository.cs`
- [ ] T114 [US12] Ensure repository implementation exists in `backend/src/InvestmentTracker.Infrastructure/Repositories/HistoricalYearEndDataRepository.cs`
- [ ] T115 [US12] Ensure service exists (cache lookup + lazy loading) in `backend/src/InvestmentTracker.Infrastructure/Services/HistoricalYearEndDataService.cs`
- [ ] T116 [US12] Ensure year-end price caching is used by performance calculations in `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs`
- [ ] T117 [US12] Ensure year-end FX caching is used by performance calculations and skips current year in `backend/src/InvestmentTracker.Infrastructure/Services/HistoricalYearEndDataService.cs`
- [ ] T118 [US12] Ensure manual price entry endpoint exists in `backend/src/InvestmentTracker.API/Controllers/MarketDataController.cs`
- [ ] T119 [US12] Ensure DI registration exists in `backend/src/InvestmentTracker.API/Program.cs`

### Frontend Implementation

- [ ] T120 [P] [US12] Create ManualPriceEntryModal component in `frontend/src/components/modals/ManualPriceEntryModal.tsx`
- [ ] T121 [US12] Integrate manual entry prompt in Performance page when API fetch fails in `frontend/src/pages/Performance.tsx`
- [ ] T122 [US12] Add API call for manual price entry in `frontend/src/services/api.ts`

**Checkpoint**: US12 complete - historical year-end prices are cached and reused

---

## Phase 15: Polish & Cross-Cutting Concerns

**Purpose**: Final improvements across all features

- [ ] T123 [P] Add backend tests for benchmark negative caching (NotAvailable persisted, no repeated Stooq calls) in `backend/tests/InvestmentTracker.API.Tests/`
- [ ] T124 [P] Add backend tests for transaction-date FX auto-fill in XIRR cashflows in `backend/tests/InvestmentTracker.API.Tests/`
- [ ] T125 [P] Add backend tests for year-end cache reuse (no duplicate API calls) in `backend/tests/InvestmentTracker.API.Tests/`
- [ ] T126 [P] Add frontend tests for benchmark selection cap (max 10) and render gate (no initial 0) in `frontend/` (Jest/RTL)
- [ ] T127 Run `specs/002-portfolio-enhancements/quickstart.md` scenarios and update if needed in `specs/002-portfolio-enhancements/quickstart.md`

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
| US10 (P2) *(NEW)* | Phase 2 only | Phase 2 complete |
| US11 (P1) *(NEW)* | US9 (Portfolio infra) | US9 complete |
| US12 (P1) *(NEW)* | Phase 2 only | Phase 2 complete |

All user stories are independent and can be implemented in parallel (except US8 depends on US3, US11 depends on US9).

### Parallel Opportunities

```text
Phase 1: T002, T003, T004 can run in parallel
Phase 2: T010, T011 can run in parallel
US2: T025 can run in parallel with backend tasks
US4: T042 can run in parallel with backend tasks
US5: T051 can run in parallel with backend tasks
US6: T055 can run in parallel
US9: T080, T081, T082 can run in parallel after backend FX wiring
US10: T097 can run in parallel with other frontend tasks
US12: T109, T110, T113, T120 can run in parallel
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
| +9 | US10 | Performance Page UX Improvements *(NEW)* |
| +10 | US11 | Portfolio Switching State Management *(NEW)* |
| +11 | US12 | Historical Year-End Price Cache *(NEW)* |

---

## Summary

| Metric | Count |
|--------|-------|
| Total Tasks | 121 |
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
| US10 Tasks *(NEW)* | 10 |
| US11 Tasks *(NEW)* | 6 |
| US12 Tasks *(NEW)* | 16 |
| Polish Tasks | 6 |
| Parallel Opportunities | 18 tasks marked [P] |

### MVP Scope (P1 Stories: US1, US7, US9, US11, US12)

| Story | Tasks | Value |
|-------|-------|-------|
| US1 | 9 | Optional exchange rate transactions |
| US7 | 3 | Auto-fetch price for new positions |
| US9 | 15 | Foreign Currency Portfolio |
| US11 | 6 | Portfolio switching state management |
| US12 | 16 | Historical year-end price cache |
| **Total MVP** | **49** | Core functionality complete |
