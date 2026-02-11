# Tasks: Dashboard & Portfolio Switching Overhaul

**Input**: Design documents from `/specs/010-dashboard-portfolio-switch/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-contracts.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure changes that MUST be complete before ANY user story can be implemented

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T001 Update PortfolioContext to support "all" sentinel value — add `isAllPortfolios` derived boolean, change default from first portfolio to `"all"` when no localStorage value exists, update `selectPortfolio` to accept `"all"`, set `currentPortfolio` to null when "all" selected, handle deleted portfolio fallback to `"all"` in `frontend/src/contexts/PortfolioContext.tsx`
- [ ] T002 Update PortfolioSelector to add "All Portfolios" as first option in dropdown — render "All Portfolios" entry before portfolio list with a distinct icon, handle click via `selectPortfolio('all')`, highlight when `isAllPortfolios` is true in `frontend/src/components/portfolio/PortfolioSelector.tsx`
- [ ] T003 [P] Add aggregate API functions to service layer — add `portfolioApi.calculateAggregateXirr(request)` for `POST /api/portfolios/aggregate/xirr`, add `performanceApi.getAggregateYears()` for `GET /api/portfolios/aggregate/performance/years`, add `performanceApi.calculateAggregateYearPerformance(request)` for `POST /api/portfolios/aggregate/performance/year`, plus corresponding TypeScript types in `frontend/src/services/api.ts`

**Checkpoint**: Foundation ready — PortfolioContext supports "all", PortfolioSelector renders "All Portfolios" option, API functions ready for backend integration

---

## Phase 2: User Story 1 + User Story 4 — Portfolio Selector & Default Behavior (Priority: P1) MVP

**Goal**: Users can switch portfolios on Dashboard and Performance pages via a dropdown selector. "All Portfolios" is the default selection. Selection is shared across pages and persists.

**Independent Test**: Navigate to Dashboard — verify selector appears with "All Portfolios" selected by default. Switch to a specific portfolio — verify data updates. Navigate to Performance — verify same portfolio is selected. Refresh browser — verify selection persists. Navigate to Portfolio page while "All" is selected — verify first portfolio is auto-selected.

### Implementation for US1 + US4

- [ ] T004 [US1] Add PortfolioSelector to Dashboard page header — render selector component in the page header area, wire up `isAllPortfolios` branch in `loadDashboardData()` to show placeholder/loading state when "all" is selected (aggregate view implemented in US2), ensure individual portfolio switching continues to work in `frontend/src/pages/Dashboard.tsx`
- [ ] T005 [P] [US1] Add PortfolioSelector to Performance page header — render selector component, add `isAllPortfolios` guard before showing "找不到投資組合" error (when "all" selected, `currentPortfolio` is null but that's valid), show placeholder state for aggregate (implemented in US3), ensure individual portfolio switching continues to work in `frontend/src/pages/Performance.tsx`
- [ ] T006 [US4] Update Portfolio management page to auto-select first portfolio when `isAllPortfolios` is true — on mount/navigation, check if `currentPortfolioId === 'all'` and call `selectPortfolio(portfolios[0].id)` to ensure Portfolio page always has a specific portfolio context in `frontend/src/pages/Portfolio.tsx`

**Checkpoint**: Portfolio selector visible and functional on Dashboard and Performance. Switching between individual portfolios works. "All Portfolios" selected by default. Selection shared across pages. Portfolio page auto-selects on "all".

---

## Phase 3: User Story 2 — Aggregate Dashboard View (Priority: P2)

**Goal**: When "All Portfolios" is selected on Dashboard, users see combined metrics (total cost, market value, unrealized PnL, XIRR), per-portfolio breakdown, merged asset allocation, aggregated net worth chart, cross-portfolio top performers, and merged recent transactions.

**Independent Test**: Select "All Portfolios" on Dashboard — verify summary cards show combined totals, per-portfolio breakdown shows each portfolio's market value, pie chart shows merged positions, historical chart shows summed monthly net worth, top performers rank across all portfolios, recent transactions merge from all portfolios.

### Backend for US2

- [ ] T007 [US2] Create CalculateAggregateXirrUseCase — follow `GetTotalAssetsSummaryUseCase` pattern: get all user portfolios, collect all transactions across portfolios, build combined cash flow series (Buy = negative, Sell = positive), use provided current prices to calculate current total value as final cash flow, invoke `PortfolioCalculator.CalculateXirr()` on combined flows, return `XirrResultDto` in `backend/src/InvestmentTracker.Application/UseCases/Portfolio/CalculateAggregateXirrUseCase.cs`
- [ ] T008 [US2] Add aggregate XIRR controller action — add `[HttpPost("aggregate/xirr")]` action to PortfoliosController that accepts `CalculatePerformanceRequest` (same as existing XIRR endpoint) and delegates to `CalculateAggregateXirrUseCase` in `backend/src/InvestmentTracker.API/Controllers/PortfoliosController.cs`

### Frontend for US2

- [ ] T009 [US2] Implement Dashboard aggregate data loading — when `isAllPortfolios`, fetch all portfolio summaries in parallel via `Promise.all(portfolios.map(p => portfolioApi.getSummary(p.id)))`, merge positions by ticker (sum shares, sum costs, merge performance data), sum `totalCostHome` and `totalValueHome` across summaries, fetch all transactions via `Promise.all(portfolios.map(p => transactionApi.getByPortfolio(p.id)))` and merge+sort by date (take top 5 for recent), compute aggregate `topPerformers` and `assetAllocation` from merged positions in `frontend/src/pages/Dashboard.tsx`
- [ ] T010 [US2] Add per-portfolio contribution breakdown section to Dashboard — when `isAllPortfolios`, render a breakdown section below summary cards showing each portfolio's name and market value (e.g., mini cards or list items), derive from individual portfolio summaries already fetched in T009 in `frontend/src/pages/Dashboard.tsx`
- [ ] T011 [US2] Implement aggregate historical net worth chart — when `isAllPortfolios`, fetch monthly net worth per portfolio via `Promise.all(portfolios.map(p => portfolioApi.getMonthlyNetWorth(p.id)))`, align months across portfolios and sum values per month, pass combined data to HistoricalValueChart component in `frontend/src/pages/Dashboard.tsx`
- [ ] T012 [US2] Implement aggregate "Fetch All Prices" — when `isAllPortfolios`, collect all unique tickers across all portfolio positions, fetch prices for each unique ticker (dedup by ticker+market), update all portfolio summaries with new prices, recompute aggregate summary in `frontend/src/pages/Dashboard.tsx`
- [ ] T013 [US2] Wire up aggregate XIRR — when `isAllPortfolios` and prices are available, call `portfolioApi.calculateAggregateXirr({ currentPrices })` from new backend endpoint, display result in XIRR summary card in `frontend/src/pages/Dashboard.tsx`

**Checkpoint**: "All Portfolios" on Dashboard shows complete aggregate view — summary cards with combined totals, per-portfolio breakdown, merged charts, cross-portfolio rankings.

---

## Phase 4: User Story 3 — Aggregate Performance View (Priority: P3)

**Goal**: When "All Portfolios" is selected on Performance page, users see combined annual performance (XIRR, Modified Dietz, TWR), aggregate year summary, benchmark comparison against combined return, and consolidated missing prices overlay.

**Independent Test**: Select "All Portfolios" on Performance — verify year selector shows union of years across all portfolios. Select a year — verify performance metrics reflect combined data. Verify benchmark comparison uses aggregate return. Check missing prices overlay consolidates from all portfolios.

### Backend for US3

- [ ] T014 [P] [US3] Create GetAggregateAvailableYearsUseCase — get all user portfolios, find earliest transaction year across all, return union of years from earliest to current year (descending), reuse `AvailableYearsDto` shape in `backend/src/InvestmentTracker.Application/UseCases/Performance/GetAggregateAvailableYearsUseCase.cs`
- [ ] T015 [P] [US3] Create CalculateAggregateYearPerformanceUseCase — get all user portfolios, collect all transactions for specified year across portfolios, calculate aggregate start/end values (sum of individual portfolio values), compute XIRR + Modified Dietz + TWR using combined transaction data and aggregate values, consolidate missing prices from all portfolios, reuse `YearPerformanceDto` shape in `backend/src/InvestmentTracker.Application/UseCases/Performance/CalculateAggregateYearPerformanceUseCase.cs`
- [ ] T016 [US3] Add aggregate performance controller actions — add `[HttpGet("aggregate/performance/years")]` and `[HttpPost("aggregate/performance/year")]` actions that delegate to new use cases, consider placing under a new `AggregatePerformanceController` or adding to existing `PerformanceController` with adjusted route prefix in `backend/src/InvestmentTracker.API/Controllers/PerformanceController.cs`

### Frontend for US3

- [ ] T017 [US3] Implement Performance aggregate data flow — when `isAllPortfolios`, call `performanceApi.getAggregateYears()` instead of per-portfolio years, call `performanceApi.calculateAggregateYearPerformance(request)` instead of per-portfolio year performance, display aggregate metrics (XIRR, Modified Dietz, TWR, year summary) using same UI components, wire benchmark comparison to use aggregate return value in `frontend/src/pages/Performance.tsx`
- [ ] T018 [US3] Implement consolidated missing prices overlay for aggregate view — when `isAllPortfolios` and aggregate year performance returns `missingPrices`, show missing prices overlay combining gaps from all portfolios, ensure manual price entry works and triggers recalculation via aggregate endpoint in `frontend/src/pages/Performance.tsx`

**Checkpoint**: "All Portfolios" on Performance shows complete aggregate annual performance — combined return metrics, aggregate year summary, benchmark comparison, consolidated missing prices.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Verify regression, edge cases, and overall quality

- [ ] T019 Verify single-portfolio regression — confirm all existing Dashboard and Performance functionality works identically when a specific portfolio is selected (no behavior change for individual portfolio views)
- [ ] T020 Verify cross-page selection consistency — confirm selecting a portfolio on Dashboard reflects on Performance and vice versa, confirm "All" on Dashboard then navigating to Portfolio auto-selects first portfolio, confirm browser refresh preserves selection
- [ ] T021 Handle edge cases — verify portfolio deletion while selected falls back to "all", verify empty state when no portfolios exist, verify loading states display correctly during aggregate data fetching, verify "Fetch All Prices" in aggregate mode works for all unique tickers

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: No dependencies — can start immediately. BLOCKS all user stories.
- **US1+US4 (Phase 2)**: Depends on Phase 1 completion. Can start immediately after.
- **US2 (Phase 3)**: Depends on Phase 1 completion. Can start after Phase 2 or in parallel with it (backend tasks T007-T008 have no frontend dependency).
- **US3 (Phase 4)**: Depends on Phase 1 completion. Can start after Phase 2. Backend tasks (T014-T016) can run in parallel with US2.
- **Polish (Phase 5)**: Depends on all user stories being complete.

### User Story Dependencies

- **US1+US4 (P1)**: Foundational context + selector changes → page integration → auto-select behavior
- **US2 (P2)**: Backend XIRR endpoint (independent) + Frontend aggregate Dashboard (depends on US1 selector being in place)
- **US3 (P3)**: Backend performance endpoints (independent) + Frontend aggregate Performance (depends on US1 selector being in place)

### Within Each User Story

- Backend tasks can run in parallel with frontend foundational tasks
- Frontend aggregate implementation depends on selector being rendered (from US1)
- API wiring depends on backend endpoints being deployed

### Parallel Opportunities

**Backend parallelism** (all backend tasks are independent of each other):
```
T007 (Aggregate XIRR UseCase)     ─┐
T008 (XIRR Controller)            ─┤ Can run in parallel
T014 (Aggregate Years UseCase)    ─┤ with each other
T015 (Aggregate Performance UC)   ─┤
T016 (Performance Controller)     ─┘
```

**Frontend parallelism** (after Phase 1):
```
T004 (Dashboard selector)  ─┐ Can run in parallel
T005 (Performance selector) ─┘ (different files)
```

**Cross-story parallelism** (after foundational):
```
US2 Backend (T007-T008)  ─┐ Can run in parallel
US3 Backend (T014-T016)  ─┘ (different files, no dependencies)
```

---

## Parallel Example: Phase 1 (Foundational)

```
# T001 and T002 are sequential (T002 depends on T001's context types)
# T003 can run in parallel with T001+T002 (different file)

Sequential: T001 (PortfolioContext) → T002 (PortfolioSelector)
Parallel:   T003 (api.ts) alongside T001+T002
```

## Parallel Example: US2 + US3 Backend

```
# All backend use cases can be implemented in parallel:
Task: "Create CalculateAggregateXirrUseCase" (T007)
Task: "Create GetAggregateAvailableYearsUseCase" (T014)
Task: "Create CalculateAggregateYearPerformanceUseCase" (T015)

# Then controllers after their use cases:
T008 after T007
T016 after T014 + T015
```

---

## Implementation Strategy

### MVP First (US1 + US4 Only)

1. Complete Phase 1: Foundational (T001-T003)
2. Complete Phase 2: US1+US4 (T004-T006)
3. **STOP and VALIDATE**: Portfolio selector works on Dashboard and Performance, switching between individual portfolios works, "All" shows placeholder
4. Deploy/demo if ready — users get portfolio switching value immediately

### Incremental Delivery

1. Foundational → selector infrastructure ready
2. US1+US4 → Portfolio switching works, "All" is default → Deploy (MVP)
3. US2 → Aggregate Dashboard view with XIRR → Deploy
4. US3 → Aggregate Performance view → Deploy
5. Polish → Edge cases verified → Final Deploy

### Suggested Batch Grouping (for team-exec)

**Batch 1**: T001 + T002 + T003 (Foundational)
**Batch 2**: T004 + T005 + T006 (US1+US4 — MVP selector)
**Batch 3**: T007 + T008 (US2 backend)
**Batch 4**: T009 + T010 + T011 (US2 frontend — aggregate summary + breakdown + chart)
**Batch 5**: T012 + T013 (US2 frontend — aggregate prices + XIRR)
**Batch 6**: T014 + T015 + T016 (US3 backend)
**Batch 7**: T017 + T018 (US3 frontend — aggregate performance)
**Batch 8**: T019 + T020 + T021 (Polish)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- No database migrations needed — all aggregation is computed
- Backend tasks follow `GetTotalAssetsSummaryUseCase` pattern for multi-portfolio aggregation
- Frontend aggregate data loading uses `Promise.all()` across portfolios for parallel fetching
- Commit after each batch for clean git history
