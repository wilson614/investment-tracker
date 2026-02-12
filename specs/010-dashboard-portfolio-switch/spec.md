# Feature Specification: Dashboard & Portfolio Switching Overhaul

**Feature Branch**: `010-dashboard-portfolio-switch`
**Created**: 2026-02-11
**Status**: Draft
**Input**: User description: "Dashboard is fixed to aggregate view (no selector). Performance keeps portfolio switching (including 'All Portfolios'). Portfolio page must not expose the 'All Portfolios' option. Also fix Performance year-switch race behavior and add MD/TWR regression validation."

## Clarifications

### Session 2026-02-11

- Q: Should the aggregate Dashboard show a per-portfolio breakdown in addition to combined totals? → A: Yes — display combined totals plus a per-portfolio contribution summary showing each portfolio's name and market value (e.g., "Portfolio A: 120k / Portfolio B: 55k").

### Session 2026-02-12

- Q: Should Dashboard still allow users to switch portfolios? → A: No — Dashboard must stay in aggregate mode and not expose a portfolio selector.
- Q: Should Portfolio page display an "All Portfolios" option? → A: No — Portfolio page must only show concrete portfolios.
- Q: How should Performance behave during rapid scope/year changes? → A: Keep the latest user intent; stale async responses must not overwrite the latest selected scope/year.
- Q: What regression case is required for annual return metrics? → A: Include a case where both Modified Dietz and TWR are present, differ numerically, and match expected values.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Portfolio Selector on Performance (Priority: P1)

A user navigating to the Performance page can switch between portfolios directly on that page without returning to the Portfolio page. The selector lists all available portfolios plus an "All Portfolios" option. Dashboard remains aggregate-only.

**Why this priority**: Performance scope switching is the core UX gap. Users need to compare individual vs aggregate annual performance without leaving the page.

**Independent Test**: Open Performance and verify the portfolio selector appears with "All Portfolios" plus all portfolios. Switch selections and verify page data updates. Open Dashboard and verify it does not show a selector and remains aggregate.

**Acceptance Scenarios**:

1. **Given** a user has 3 portfolios, **When** they open the Performance page, **Then** a portfolio selector appears showing "All Portfolios" (selected by default), "Portfolio A", "Portfolio B", "Portfolio C".
2. **Given** a user is on Performance with "Portfolio A" selected, **When** they select "Portfolio B" from the dropdown, **Then** the Performance content updates to show Portfolio B's data.
3. **Given** a user selects "Portfolio B" on Performance, **When** they navigate to Dashboard, **Then** Dashboard still shows aggregate data and no portfolio selector.
4. **Given** a user selects "All Portfolios" on Performance, **When** they navigate to Portfolio page, **Then** Portfolio page resolves to a specific portfolio context and does not display an "All Portfolios" choice.

---

### User Story 2 - Fixed Aggregate Dashboard View (Priority: P2)

Dashboard always shows combined investment metrics across all portfolios: total cost, total market value, total unrealized gain/loss, overall XIRR, combined asset allocation, and aggregated historical net worth chart.

**Why this priority**: Dashboard is the default overview surface. Fixing it to aggregate mode gives a stable whole-portfolio snapshot.

**Independent Test**: Open Dashboard and verify that summary metrics, charts, and holdings reflect data from all portfolios combined, and no portfolio selector is shown.

**Acceptance Scenarios**:

1. **Given** a user has 2 portfolios (A: cost 100k, market value 120k; B: cost 50k, market value 55k), **When** they open Dashboard, **Then** Dashboard shows total cost 150k, total market value 175k, total unrealized gain 25k, plus a per-portfolio contribution summary showing each portfolio's name and market value.
2. **Given** Dashboard aggregate mode, **When** the user views the asset allocation pie chart, **Then** the chart shows combined allocation across all portfolios (holdings from all portfolios merged).
3. **Given** Dashboard aggregate mode, **When** the user views the historical net worth chart, **Then** the chart shows the sum of monthly net worth values across all portfolios.
4. **Given** Dashboard aggregate mode, **When** the user views top performers and recent transactions, **Then** holdings are ranked across all portfolios, and recent transactions show the most recent entries from any portfolio.
5. **Given** a user has 0 portfolios, **When** they open Dashboard, **Then** Dashboard shows an empty state with a prompt to create a portfolio.

---

### User Story 3 - Aggregate Performance View (Priority: P3)

When "All Portfolios" is selected on the Performance page, the user sees combined annual performance metrics calculated across all portfolios. This includes fund-weighted return (Modified Dietz), time-weighted return (TWR), and benchmark comparison using combined data.

**Why this priority**: Aggregate performance analysis completes the "whole portfolio" picture. Users can see their overall investment performance without manually averaging across portfolios.

**Independent Test**: Select "All Portfolios" on Performance, choose a year, and verify that performance metrics and benchmark comparisons reflect combined portfolio data, including parity with a single active portfolio and mixed TWD+USD contribution reconciliation.

**Acceptance Scenarios**:

1. **Given** "All Portfolios" is selected on Performance page, **When** the user selects year 2025, **Then** the system calculates performance using all transactions across all portfolios for that year.
2. **Given** "All Portfolios" is selected, **When** viewing the year summary, **Then** starting value is the sum of all portfolio starting values, ending value is the sum of all portfolio ending values, and net contributions is the sum across all portfolios.
3. **Given** "All Portfolios" is selected, **When** viewing benchmark comparison, **Then** the user's combined return is compared against the same benchmarks available for individual portfolios.
4. **Given** "All Portfolios" is selected and a year has missing prices for some holdings, **When** the missing prices overlay appears, **Then** it shows all missing prices across all portfolios combined.
5. **Given** aggregate available years is requested and the user has no portfolios (or portfolios without non-deleted transactions), **When** the endpoint responds, **Then** it returns an empty `AvailableYearsDto` (`years: []`, `earliestYear: null`, `currentYear: current UTC year`) instead of a not-found error.
6. **Given** one portfolio has in-year activity and other portfolios are empty, **When** aggregate year performance is calculated, **Then** aggregate source currency, start/end values, net contributions, and return metrics match the active portfolio result.
7. **Given** the user rapidly switches performance scope/year, **When** async responses return out of order, **Then** the UI keeps the latest selected scope/year and ignores stale responses.
8. **Given** a regression fixture where both market drift and timed contributions exist, **When** annual performance is computed, **Then** Modified Dietz and TWR are both present, numerically different, and each matches expected values within tolerance.

---

### User Story 4 - Selection Persistence with Portfolio-Page Constraints (Priority: P1)

Performance defaults to "All Portfolios" when there is no persisted selection. Selection persistence remains supported. Portfolio page only supports specific portfolios and must not expose "All Portfolios" as a selectable option.

**Why this priority**: Consistent state and clear page responsibility reduce confusion. Performance supports scope switching; Portfolio page remains portfolio-specific for management workflows.

**Independent Test**: Clear localStorage, open Performance (should show "All"), select a specific portfolio and refresh (selection persists), then navigate to Portfolio page and verify "All" is not shown.

**Acceptance Scenarios**:

1. **Given** a new user (no prior selection stored), **When** they open Performance, **Then** "All Portfolios" is selected by default.
2. **Given** the user previously selected "Portfolio A", **When** they return to Performance after browser refresh, **Then** "Portfolio A" is still selected (persisted in storage).
3. **Given** the user selects "All Portfolios" on Performance, **When** they navigate to Portfolio management page, **Then** the Portfolio page auto-selects a specific portfolio and does not show an "All Portfolios" option.
4. **Given** the user selects a specific portfolio on the Portfolio management page, **When** they navigate to Performance, **Then** the Performance page shows that specific portfolio selected.

---

### Edge Cases

- What happens when a portfolio is deleted while "All Portfolios" is selected on Performance? Performance aggregate re-calculates without the deleted portfolio; Dashboard aggregate re-calculates as well.
- What happens when a portfolio is deleted while that specific portfolio is selected? Selection falls back to "All Portfolios".
- What happens when the only portfolio is deleted? Dashboard and Performance show empty aggregate states.
- How does the aggregate view handle portfolios with different base currencies? All portfolios are assumed to use the same base currency (TWD), consistent with the existing Total Assets aggregation logic.
- What happens when price data is loading for aggregate view? Show loading indicators on affected metrics while data loads progressively.
- What happens to the "Fetch All Prices" action in aggregate view? It fetches prices for all holdings across all portfolios.
- How should UI behave under rapid scope/year interactions on Performance? It must render only the latest user intent and avoid stale overwrite.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST render Dashboard as a fixed aggregate view and MUST NOT show a portfolio selector on Dashboard.
- **FR-002**: System MUST display a portfolio selector dropdown on the Performance page header, listing all user portfolios plus an "All Portfolios" option.
- **FR-003**: Portfolio selection state MUST be shared between Performance and Portfolio pages for specific portfolio IDs; Dashboard remains aggregate-only and does not participate in selector switching.
- **FR-004**: The "All Portfolios" selection MUST be the default for Performance when no prior selection exists.
- **FR-005**: Portfolio selection MUST persist across browser refreshes (stored in local storage).
- **FR-006**: Dashboard MUST display aggregated metrics: total cost, total market value, total unrealized gain/loss, and overall XIRR across all portfolios, plus a per-portfolio contribution summary showing each portfolio's name and market value.
- **FR-007**: Dashboard historical net worth chart MUST show combined monthly net worth values across all portfolios.
- **FR-008**: Dashboard asset allocation chart MUST show the combined allocation from all portfolios.
- **FR-009**: Dashboard top performers MUST rank holdings across all portfolios, and recent transactions MUST show the latest entries from any portfolio.
- **FR-010**: When "All Portfolios" is selected on Performance, the system MUST calculate combined annual performance (Modified Dietz and TWR) using transactions from all portfolios.
- **FR-011**: When "All Portfolios" is selected on Performance, the year summary MUST show aggregated starting value, ending value, and net contributions across all portfolios.
- **FR-012**: When "All Portfolios" is selected on Performance, benchmark comparison MUST compare combined portfolio return against the same benchmarks available for individual portfolios.
- **FR-013**: When "All Portfolios" is selected on Performance and there are missing prices, the system MUST show a consolidated missing prices overlay combining gaps from all portfolios.
- **FR-014**: Aggregate available years endpoint MUST return an empty `AvailableYearsDto` (`years: []`, `earliestYear: null`, `currentYear`) when the user has no portfolios or no non-deleted transactions across portfolios, and MUST NOT return a not-found error for this empty state.
- **FR-015**: Aggregate year performance in a "single active portfolio + other empty portfolios" setup MUST match the active portfolio for source currency, start/end values, net contributions, and return metrics.
- **FR-016**: Performance year selection MUST preserve the user-selected year when switching portfolio scope if that year exists in the new scope; fallback to `currentYear` or first available year is allowed only when the selected year is unavailable. The UI MUST ignore stale async responses so out-of-order requests cannot override the latest selected scope/year.
- **FR-017**: Portfolio management page MUST continue to require a specific portfolio selection and MUST NOT display an "All Portfolios" option. If "All Portfolios" is the current selection when navigating to Portfolio page, the first available portfolio should be auto-selected.
- **FR-018**: When the currently selected specific portfolio is deleted, the system MUST fall back to "All Portfolios" selection.
- **FR-019**: Regression coverage MUST include a case where both Modified Dietz and TWR are present, numerically different, and validated against expected values within tolerance.

### Key Entities

- **Portfolio Selection State**: Represents the user's current Performance scope, which can be either a specific portfolio ID or a special "all" value. Persisted in local storage and shared where applicable.
- **Aggregated Summary**: A combined view of investment metrics across multiple portfolios — total cost, market value, unrealized gain, and XIRR.
- **Aggregated Performance**: Combined annual performance metrics (MWR, TWR) calculated from all transactions across all portfolios for a given year.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can switch portfolio scope on Performance without navigating to Portfolio page.
- **SC-002**: Dashboard aggregate view loads and displays combined metrics from all portfolios within the same perceived performance as individual portfolio views.
- **SC-003**: Selection state remains consistent between Performance and Portfolio pages for specific portfolios, while Dashboard consistently remains aggregate-only.
- **SC-004**: Aggregate Performance produces correct combined annual returns that can be verified against manually calculated cross-portfolio performance.
- **SC-005**: All existing single-portfolio functionality on Dashboard and Performance continues to work identically when a specific portfolio is selected on Performance.
- **SC-006**: Regression tests include at least one scenario where Modified Dietz and TWR are both available, differ numerically, and match expected values.

## Assumptions

- All portfolios belong to a single user and use the same base currency (TWD).
- The existing Total Assets aggregation patterns can be extended or adapted for Dashboard and Performance aggregation.
- The portfolio selector UI can reuse the existing PortfolioSelector component, with page-level behavior differences (Performance supports "All"; Portfolio page does not).
- XIRR for the aggregate view will be calculated by combining all transactions from all portfolios as if they were a single portfolio.
- Historical monthly net worth for the aggregate view will be the sum of individual portfolio monthly net worth values.

## Out of Scope

- Multi-currency support across portfolios (all portfolios assumed TWD base).
- Aggregated views on pages other than Dashboard and Performance (e.g., no aggregate view on Transactions or Holdings detail pages).
- Custom portfolio grouping or tagging beyond "All" vs individual.
- Backend dashboard microservice or dedicated dashboard API — the existing API composition pattern (multiple API calls assembled by frontend) may be extended but a new dedicated endpoint is optional, not required.
