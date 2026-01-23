# Feature Specification: Performance Refinements

**Feature Branch**: `004-performance-refinements`
**Created**: 2026-01-23
**Status**: Draft
**Input**: Performance analysis optimization including return calculation adjustments, multi-currency display support, monthly historical net worth data, and authentication mechanism fixes.

## Clarifications

### Session 2026-01-23

- Q: What should the default Access Token expiration duration be? → A: 120 minutes
- Q: How many months of historical data should the monthly net worth chart display? → A: All available history (from first transaction to present)
- Q: Should we use "Source Currency" or "Base Currency" as the canonical term? → A: Source Currency (paired with Home Currency)
- Q: How should we handle the current month on the monthly net worth chart? → A: Include current month "as of today" (use today's date / nearest trading day for price), label still uses YYYY-MM
- Q: What should the chart "Contributions" line represent? → A: Cumulative net contributions (Buy - Sell) in home currency
- Q: Which FX rate should be used when converting month-end valuations to home currency? → A: Month-end FX rate (nearest trading day on or before valuation date)
- Q: Month-end price strategy? → A: Fetch full daily series per ticker for the requested range and derive month-end points server-side (Yahoo primary; fallback as needed)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Annual Simple Return (Priority: P1)

As an investor, I want to see the annual "Simple Return" instead of XIRR on the performance page, so I can understand my yearly investment performance more intuitively without being misled by annualized short-term data.

**Why this priority**: This is the core metric on the performance page, directly affecting how users understand investment performance. XIRR is suitable for long-term calculations, while simple return is more intuitive for single-year analysis.

**Independent Test**: Enter the performance page, select any year, and the system should display the simple return rate instead of XIRR.

**Acceptance Scenarios**:

1. **Given** user has transaction records for 2024, **When** user enters the performance page and selects 2024, **Then** system displays the 2024 simple return rate (both source and home currency versions)
2. **Given** user has holdings at year start and buy/sell transactions during the year, **When** user views that year's performance, **Then** simple return is correctly calculated as (End Value - Start Value - Net Contributions) / (Start Value + Net Contributions) × 100%
3. **Given** user is in their first investment year (no start value), **When** user views that year's performance, **Then** simple return is calculated as (End Value - Net Contributions) / Net Contributions × 100%

---

### User Story 2 - Toggle Performance Comparison Currency Mode (Priority: P1)

As an investor, I want to toggle between "Source Currency Comparison" and "Home Currency Comparison" modes in the performance comparison bar chart, so I can separately observe stock performance versus overall performance including currency fluctuations.

**Why this priority**: Performance comparison is a critical function for evaluating investment results. Currency toggle allows users to distinguish between stock performance and currency impact.

**Independent Test**: Toggle the switch in the performance comparison section, and the bar chart should display return data in the corresponding currency.

**Acceptance Scenarios**:

1. **Given** user is in the performance comparison section, **When** user toggles to "Source Currency Comparison" mode, **Then** bar chart displays my return (USD) versus benchmark returns
2. **Given** user is in the performance comparison section, **When** user toggles to "Home Currency Comparison" mode, **Then** bar chart displays my return (TWD) versus benchmark returns
3. **Given** user toggles the currency mode, **When** toggle is complete, **Then** system remembers user preference (maintains same setting on next page visit)

---

### User Story 3 - View Source Currency Unrealized P&L (Priority: P2)

As an investor, I want to see unrealized profit/loss in source currency on the position detail page, so I can understand the stock's actual performance without being affected by currency fluctuations.

**Why this priority**: Source currency P&L helps users distinguish between "stock performance" and "currency gains/losses", which is important information for advanced performance analysis.

**Independent Test**: Enter any position detail page, and it should display both source currency P&L and home currency P&L.

**Acceptance Scenarios**:

1. **Given** user enters a US stock position detail page, **When** page loads completely, **Then** displays source currency P&L (e.g., +$1,200 USD (+15%)) and home currency P&L
2. **Given** user enters a Taiwan stock position detail page, **When** page loads completely, **Then** source currency P&L and home currency P&L display the same values (since Taiwan stocks are priced in TWD)
3. **Given** position has a loss, **When** user views details, **Then** source currency P&L displays as negative in red (e.g., -$500 USD (-8%))

---

### User Story 4 - View Monthly Net Worth Changes (Priority: P2)

As an investor, I want to see monthly data points in the historical net worth chart, so I can track my portfolio value changes more precisely.

**Why this priority**: Monthly data reflects portfolio fluctuations and growth trajectory better than yearly data, helping users observe trends.

**Independent Test**: View the historical net worth chart on the dashboard, and the X-axis should display monthly labels (e.g., 2024-01, 2024-02).

**Acceptance Scenarios**:

1. **Given** user has transaction records spanning more than one year, **When** user views the historical net worth chart, **Then** X-axis displays monthly labels (YYYY-MM format) for all available history from first transaction
2. **Given** user has multiple holdings, **When** system calculates month-end net worth, **Then** system uses each holding's closing price at month-end to calculate total value
3. **Given** no transactions occurred in a particular month, **When** system calculates that month's end value, **Then** system can still retrieve month-end prices for holdings and display correctly
4. **Given** the current month is not finished, **When** system returns the latest monthly point, **Then** it includes the current month "as-of today" valuation (prices and FX use the nearest trading day on or before today)
5. **Given** the chart shows the Contributions line, **When** user inspects it, **Then** it represents cumulative net contributions (Buy - Sell) in home currency over time

---

### User Story 5 - View Benchmark Annual Total Return (Priority: P3)

As an investor, I want to see benchmark ETF annual total return (including dividends) in historical performance comparison, so the comparison is more accurate and complete.

**Why this priority**: Using Yahoo's Annual Total Return data includes dividends, more accurately reflecting ETF's true return compared to price-only changes.

**Independent Test**: View historical year performance comparison, and benchmark returns should reflect Total Return (including dividends).

**Acceptance Scenarios**:

1. **Given** user selects 2023 annual performance, **When** system loads benchmark returns, **Then** benchmark returns come from Yahoo Annual Total Return data
2. **Given** Yahoo data is unavailable, **When** system attempts to retrieve benchmark returns, **Then** system falls back to existing year-end price snapshot calculation method
3. **Given** user selects current year (YTD), **When** system loads benchmark returns, **Then** YTD still uses existing calculation method (Sina real-time + Yahoo historical), unaffected by this change

---

### User Story 6 - Maintain Login Session (Priority: P3)

As a user, I want to stay logged in during normal usage without being frequently logged out, so I can use the system smoothly without repeated logins.

**Why this priority**: Frequent logouts severely impact user experience. Authentication mechanism should ensure reasonable session duration.

**Independent Test**: Use the system normally in production environment, and user should maintain reasonable login state.

**Acceptance Scenarios**:

1. **Given** user is logged in and actively using the system, **When** Access Token is about to expire, **Then** system automatically uses Refresh Token to update Access Token without user awareness
2. **Given** user is idle beyond Access Token validity period (120 minutes), **When** user performs an action, **Then** system automatically attempts refresh; if successful, continues operation; if failed, requires re-login
3. **Given** system runs in Docker production environment, **When** Token expiration settings take effect, **Then** Access Token validity defaults to 120 minutes (configurable via environment variables)

---

### Edge Cases

- No transactions in certain months: System should still calculate month-end net worth for holdings
- New stock purchased mid-month: Month-end value should include the new stock's value
- Yahoo historical data unavailable: System should fall back to existing calculation method and indicate in UI that data source may be incomplete
- Refresh Token expired: System should gracefully redirect user to login page
- User logged in on multiple devices: Refresh Token mechanism should correctly handle multi-device scenarios
- Large historical data range: Chart should handle displaying many months of data without performance degradation

## Requirements *(mandatory)*

### Functional Requirements

#### Annual Return Calculation

- **FR-001**: System MUST display "Simple Return" instead of XIRR in performance page annual cards
- **FR-002**: System MUST calculate simple return using formula: (End Value - Start Value - Net Contributions) / (Start Value + Net Contributions) × 100%
- **FR-003**: System MUST use formula for first investment year (no start value): (End Value - Net Contributions) / Net Contributions × 100%
- **FR-004**: System MUST display both source currency and home currency simple returns

#### Performance Comparison Currency Toggle

- **FR-005**: System MUST provide currency toggle in performance comparison bar chart section
- **FR-006**: System MUST support "Source Currency Comparison" mode displaying source currency return vs benchmarks
- **FR-007**: System MUST support "Home Currency Comparison" mode displaying home currency return vs benchmarks
- **FR-008**: System MUST persist user's currency preference setting

#### Source Currency Unrealized P&L

- **FR-009**: System MUST display source currency unrealized P&L amount on position detail page
- **FR-010**: System MUST display source currency unrealized P&L percentage on position detail page
- **FR-011**: System MUST use correct colors for profit (green) and loss (red)

#### Monthly Net Worth Changes

- **FR-012**: System MUST change historical net worth chart from yearly to monthly data
- **FR-013**: System MUST retrieve each holding's closing price at month-end
- **FR-014**: System MUST use Yahoo Finance as primary data source, Stooq as fallback
- **FR-015**: System MUST cache month-end price data to avoid repeated requests; cache MUST be invalidated when transactions are added, edited, or deleted from the affected month onwards
- **FR-016**: System MUST display all available monthly history from first transaction to present
- **FR-016a**: System MUST include the current month point using an "as-of today" valuation (label still YYYY-MM), using nearest trading day on or before today for both price and FX
- **FR-016b**: System MUST compute and display cumulative net contributions (Buy - Sell) in home currency for the Contributions line
- **FR-016c**: System MUST convert valuations to home currency using month-end FX rate (nearest trading day on or before the valuation date)
- **FR-016d**: System SHOULD fetch full daily series per ticker for the requested range and derive month-end points server-side to reduce external API calls

#### Historical Benchmark Returns

- **FR-017**: System MUST use Yahoo Annual Total Return data as source for historical benchmark returns
- **FR-018**: System MUST fall back to existing calculation method when Yahoo data is unavailable
- **FR-019**: System MUST maintain YTD calculation logic unchanged (Sina real-time + Yahoo historical)

#### Authentication Mechanism

- **FR-020**: System MUST automatically execute refresh when Access Token is about to expire
- **FR-021**: System MUST correctly handle 401 responses and attempt to update using Refresh Token
- **FR-022**: System MUST default Access Token expiration to 120 minutes
- **FR-023**: System MUST support Token expiration configuration via environment variables
- **FR-024**: System MUST redirect user to login page when Refresh Token expires

### Terminology

- **Source Currency**: The currency in which stocks are originally priced (e.g., USD for US stocks). Paired with "Home Currency".
- **Home Currency**: The user's local currency for portfolio valuation (e.g., TWD). Paired with "Source Currency".

### Key Entities

- **MonthlySnapshot**: Month-end net worth snapshot including date, individual holding prices, total value
- **BenchmarkAnnualReturn**: Benchmark annual total return with source indicator (Yahoo / calculated)
- **UserPreference**: User preference settings including performance comparison currency mode

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can see intuitive annual simple return on performance page without needing to understand XIRR concept
- **SC-002**: Users can switch performance comparison currency mode within 2 clicks
- **SC-003**: Users can see both source and home currency P&L on position detail page without additional calculations
- **SC-004**: Historical net worth chart displays all available monthly data points from first transaction
- **SC-005**: Historical benchmark returns display Total Return including dividends, consistent with public information
- **SC-006**: Users are not logged out during normal usage period (within 2 hours with default settings)
- **SC-007**: Token auto-refresh mechanism works transparently (user does not experience unexpected logouts during normal network conditions)

## Assumptions

- User's portfolio uses TWD as home currency
- User's portfolio uses USD as source currency
- Yahoo Finance provides stable Annual Total Return data (fallback mechanism used if unstable)
- Month-end closing price uses the last trading day's closing price of each month
- Docker production environment supports environment variable configuration for JWT parameters
