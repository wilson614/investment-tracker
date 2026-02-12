# Feature Specification: Dashboard & Portfolio Switching Overhaul

**Feature Branch**: `010-dashboard-portfolio-switch`
**Created**: 2026-02-11
**Status**: Draft
**Input**: User description: "Add portfolio selector with 'All' option to Dashboard and Performance pages. Dashboard defaults to aggregate view. Performance supports both individual and aggregate."

## Clarifications

### Session 2026-02-11

- Q: Should the aggregate Dashboard show a per-portfolio breakdown in addition to combined totals? → A: Yes — display combined totals plus a per-portfolio contribution summary showing each portfolio's name and market value (e.g., "Portfolio A: 120k / Portfolio B: 55k").

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Portfolio Selector on Dashboard and Performance (Priority: P1)

A user navigating to the Dashboard or Performance page can switch between portfolios directly on those pages without returning to the Portfolio page. A dropdown selector appears in the page header area, listing all available portfolios plus an "All Portfolios" option.

**Why this priority**: This is the core UX gap — users currently must navigate away to switch portfolios, breaking their workflow. Adding the selector to both pages removes the biggest friction point.

**Independent Test**: Can be tested by navigating to Dashboard or Performance and verifying the portfolio selector dropdown appears, lists all portfolios plus "All Portfolios", and switching between them updates the page content.

**Acceptance Scenarios**:

1. **Given** a user has 3 portfolios, **When** they open the Dashboard page, **Then** a portfolio selector dropdown appears showing "All Portfolios" (selected by default), "Portfolio A", "Portfolio B", "Portfolio C".
2. **Given** a user is on the Dashboard with "Portfolio A" selected, **When** they select "Portfolio B" from the dropdown, **Then** the Dashboard content updates to show Portfolio B's data.
3. **Given** a user selects "Portfolio B" on the Dashboard, **When** they navigate to the Performance page, **Then** the Performance page also shows "Portfolio B" selected (selection is shared).
4. **Given** a user has only 1 portfolio, **When** they open the Dashboard, **Then** the selector still appears showing "All Portfolios" and the single portfolio name.

---

### User Story 2 - Aggregate Dashboard View (Priority: P2)

When "All Portfolios" is selected on the Dashboard, the user sees combined investment metrics across all their portfolios: total cost, total market value, total unrealized gain/loss, overall XIRR, combined asset allocation, and aggregated historical net worth chart.

**Why this priority**: The aggregate view transforms the Dashboard from a single-portfolio tool to a whole-portfolio overview, which is the primary use case for most investors checking their dashboard.

**Independent Test**: Can be tested by selecting "All Portfolios" on the Dashboard and verifying that summary metrics, charts, and holdings reflect data from all portfolios combined.

**Acceptance Scenarios**:

1. **Given** a user has 2 portfolios (A: cost 100k, market value 120k; B: cost 50k, market value 55k), **When** "All Portfolios" is selected, **Then** Dashboard shows total cost 150k, total market value 175k, total unrealized gain 25k, plus a per-portfolio contribution summary showing each portfolio's name and market value.
2. **Given** "All Portfolios" is selected, **When** the user views the asset allocation pie chart, **Then** the chart shows combined allocation across all portfolios (holdings from all portfolios merged).
3. **Given** "All Portfolios" is selected, **When** the user views the historical net worth chart, **Then** the chart shows the sum of monthly net worth values across all portfolios.
4. **Given** "All Portfolios" is selected, **When** the user views top performers and recent transactions, **Then** holdings are ranked across all portfolios, and recent transactions show the most recent entries from any portfolio.
5. **Given** a user has 0 portfolios, **When** "All Portfolios" is selected, **Then** Dashboard shows an empty state with a prompt to create a portfolio.

---

### User Story 3 - Aggregate Performance View (Priority: P3)

When "All Portfolios" is selected on the Performance page, the user sees combined annual performance metrics calculated across all portfolios. This includes fund-weighted return (Modified Dietz), time-weighted return (TWR), and benchmark comparison using combined data.

**Why this priority**: Aggregate performance analysis completes the "whole portfolio" picture. Users can see their overall investment performance without manually averaging across portfolios.

**Independent Test**: Can be tested by selecting "All Portfolios" on the Performance page, choosing a year, and verifying that performance metrics and benchmark comparisons reflect combined portfolio data, including parity with a single active portfolio and mixed TWD+USD contribution reconciliation.

**Acceptance Scenarios**:

1. **Given** "All Portfolios" is selected on Performance page, **When** the user selects year 2025, **Then** the system calculates performance using all transactions across all portfolios for that year.
2. **Given** "All Portfolios" is selected, **When** viewing the year summary, **Then** starting value is the sum of all portfolio starting values, ending value is the sum of all portfolio ending values, and net contributions is the sum across all portfolios.
3. **Given** "All Portfolios" is selected, **When** viewing benchmark comparison, **Then** the user's combined return is compared against the same benchmarks available for individual portfolios.
4. **Given** "All Portfolios" is selected and a year has missing prices for some holdings, **When** the missing prices overlay appears, **Then** it shows all missing prices across all portfolios combined.
5. **Given** aggregate available years is requested and the user has no portfolios (or portfolios without non-deleted transactions), **When** the endpoint responds, **Then** it returns an empty `AvailableYearsDto` (`years: []`, `earliestYear: null`, `currentYear: current UTC year`) instead of a not-found error.
6. **Given** one portfolio has in-year activity and other portfolios are empty, **When** aggregate year performance is calculated, **Then** aggregate source currency, start/end values, net contributions, and return metrics match the active portfolio result.

---

### User Story 4 - Default Selection Behavior (Priority: P1)

The Dashboard defaults to "All Portfolios" view when the user first visits (no prior selection stored). The Performance page defaults to the same selection as Dashboard (shared state). When a user selects a specific portfolio on any page (Dashboard, Performance, or Portfolio page), all pages reflect that selection.

**Why this priority**: Consistent default behavior and shared selection state prevent confusion when navigating between pages. "All Portfolios" as default gives the best first impression of overall portfolio health.

**Independent Test**: Can be tested by clearing localStorage, opening Dashboard (should show "All"), then selecting a portfolio and navigating between pages to verify shared state.

**Acceptance Scenarios**:

1. **Given** a new user (no prior selection stored), **When** they open the Dashboard, **Then** "All Portfolios" is selected by default.
2. **Given** the user previously selected "Portfolio A", **When** they return to Dashboard after browser refresh, **Then** "Portfolio A" is still selected (persisted in storage).
3. **Given** the user selects "All Portfolios" on Dashboard, **When** they navigate to Portfolio management page, **Then** the Portfolio page shows the first portfolio (since portfolio management requires a specific portfolio).
4. **Given** the user selects a specific portfolio on the Portfolio management page, **When** they navigate to Dashboard, **Then** the Dashboard shows that specific portfolio selected.

---

### Edge Cases

- What happens when a portfolio is deleted while "All Portfolios" is selected? The aggregate view re-calculates without the deleted portfolio.
- What happens when a portfolio is deleted while that specific portfolio is selected? Selection falls back to "All Portfolios".
- What happens when the only portfolio is deleted? "All Portfolios" shows empty state.
- How does the aggregate view handle portfolios with different base currencies? All portfolios are assumed to use the same base currency (TWD), consistent with the existing Total Assets aggregation logic.
- What happens when price data is loading for aggregate view? Show loading indicators on affected metrics while data loads progressively.
- What happens to the "Fetch All Prices" action in aggregate view? It fetches prices for all holdings across all portfolios.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display a portfolio selector dropdown on the Dashboard page header, listing all user portfolios plus an "All Portfolios" option.
- **FR-002**: System MUST display a portfolio selector dropdown on the Performance page header, with the same options as Dashboard.
- **FR-003**: Portfolio selection MUST be shared across Dashboard, Performance, and Portfolio pages — selecting on one page updates the others.
- **FR-004**: The "All Portfolios" selection MUST be the default when no prior selection exists.
- **FR-005**: Portfolio selection MUST persist across browser refreshes (stored in local storage).
- **FR-006**: When "All Portfolios" is selected on Dashboard, the system MUST display aggregated metrics: total cost, total market value, total unrealized gain/loss, and overall XIRR across all portfolios, plus a per-portfolio contribution summary showing each portfolio's name and market value.
- **FR-007**: When "All Portfolios" is selected on Dashboard, the historical net worth chart MUST show combined monthly net worth values across all portfolios.
- **FR-008**: When "All Portfolios" is selected on Dashboard, the asset allocation chart MUST show the combined allocation from all portfolios.
- **FR-009**: When "All Portfolios" is selected on Dashboard, top performers MUST rank holdings across all portfolios, and recent transactions MUST show the latest entries from any portfolio.
- **FR-010**: When "All Portfolios" is selected on Performance, the system MUST calculate combined annual performance (Modified Dietz and TWR) using transactions from all portfolios.
- **FR-011**: When "All Portfolios" is selected on Performance, the year summary MUST show aggregated starting value, ending value, and net contributions across all portfolios.
- **FR-012**: When "All Portfolios" is selected on Performance, benchmark comparison MUST compare combined portfolio return against the same benchmarks available for individual portfolios.
- **FR-013**: When "All Portfolios" is selected on Performance and there are missing prices, the system MUST show a consolidated missing prices overlay combining gaps from all portfolios.
- **FR-014**: Aggregate available years endpoint MUST return an empty `AvailableYearsDto` (`years: []`, `earliestYear: null`, `currentYear`) when the user has no portfolios or no non-deleted transactions across portfolios, and MUST NOT return a not-found error for this empty state.
- **FR-015**: Aggregate year performance in a "single active portfolio + other empty portfolios" setup MUST match the active portfolio for source currency, start/end values, net contributions, and return metrics.
- **FR-016**: Performance year selection MUST preserve the user-selected year when switching portfolio scope (specific ↔ all or specific ↔ specific) if that year exists in the new scope; fallback to `currentYear` or first available year is allowed only when the selected year is not available.
- **FR-017**: The Portfolio management page MUST continue to require a specific portfolio selection (not "All Portfolios"). If "All Portfolios" is the current selection when navigating to Portfolio page, the first portfolio should be auto-selected.
- **FR-018**: When the currently selected portfolio is deleted, the system MUST fall back to "All Portfolios" selection.

### Key Entities

- **Portfolio Selection State**: Represents the user's current portfolio choice, which can be either a specific portfolio ID or a special "all" value. Persisted in local storage and shared across pages via application state.
- **Aggregated Summary**: A combined view of investment metrics across multiple portfolios — total cost, market value, unrealized gain, and XIRR. Derived by merging data from individual portfolio summaries.
- **Aggregated Performance**: Combined annual performance metrics (MWR, TWR) calculated from all transactions across all portfolios for a given year.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can switch portfolios on Dashboard and Performance pages without navigating to the Portfolio page. Task completion (switching portfolio context) reduces from 3+ clicks/navigations to 1 click.
- **SC-002**: The "All Portfolios" aggregate Dashboard view loads and displays combined metrics from all portfolios within the same perceived performance as individual portfolio views.
- **SC-003**: Portfolio selection state remains consistent across all pages — selecting a portfolio on any page is immediately reflected when navigating to another page.
- **SC-004**: The aggregate Performance view produces correct combined annual returns that can be verified against manually calculated cross-portfolio performance.
- **SC-005**: All existing single-portfolio functionality on Dashboard and Performance pages continues to work identically when a specific portfolio is selected.

## Assumptions

- All portfolios belong to a single user and use the same base currency (TWD).
- The existing Total Assets aggregation patterns can be extended or adapted for Dashboard and Performance aggregation.
- The portfolio selector UI can reuse the existing PortfolioSelector component with modifications to support the "All Portfolios" option.
- XIRR for the aggregate view will be calculated by combining all transactions from all portfolios as if they were a single portfolio.
- Historical monthly net worth for the aggregate view will be the sum of individual portfolio monthly net worth values.
- The existing portfolio selection state can be extended to support "All Portfolios" as a valid selection.

## Out of Scope

- Multi-currency support across portfolios (all portfolios assumed TWD base).
- Aggregated views on pages other than Dashboard and Performance (e.g., no aggregate view on Transactions or Holdings detail pages).
- Custom portfolio grouping or tagging beyond "All" vs individual.
- Backend dashboard microservice or dedicated dashboard API — the existing API composition pattern (multiple API calls assembled by frontend) may be extended but a new dedicated endpoint is optional, not required.
