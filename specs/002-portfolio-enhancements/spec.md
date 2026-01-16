# Feature Specification: Portfolio Enhancements V2

**Feature Branch**: `002-portfolio-enhancements`
**Created**: 2026-01-14
**Updated**: 2026-01-16
**Status**: Draft
**Input**: 12 portfolio enhancement features

## Overview

This module contains 12 enhancements to the existing investment portfolio tracking system, covering transaction flexibility, visualization improvements, exchange support expansion, performance tracking capabilities, multi-portfolio support for foreign currency investments, and historical price caching for improved reliability.

## Clarifications

### Session 2026-01-14

- Q: How to calculate average cost for mixed exchange rate transactions? → A: Track separately: transactions with exchange rate calculate in TWD, transactions without exchange rate calculate in source currency, do not merge
- Q: How to handle Euronext API failures? → A: Display last successfully fetched cached price, marked as "stale"
- Q: How to handle missing year-end closing prices for historical years? → A: Prompt user to manually input missing year-end closing prices
- Q: Default behavior when ETF type cannot be determined? → A: Default to accumulating type (no dividend adjustment), marked as "unconfirmed type". Note: Currently only Taiwan stocks support dividend adjustment; others should use accumulating ETFs for accurate data; future consideration to add US stock dividend adjustment or fetch reliable sources for annual total return

### Session 2026-01-15

- Q: Foreign Currency Portfolio base currency handling? → A: Single currency mode - each Foreign Currency Portfolio has one designated Base Currency (e.g., USD), only stocks denominated in that currency can be added
- Q: Pie chart asset classification basis? → A: By market/exchange - Taiwan stocks, US stocks, UK stocks, Euronext each as separate segments
- Q: Historical year selector range? → A: Dynamic range - only show years where user has transaction records

### Session 2026-01-16

- Q: What metrics to display on performance page? → A: Only XIRR is meaningful; total return percentage cards removed. Yearly summary (value sections) retained for reference.
- Q: Performance comparison format? → A: Compare portfolio XIRR (USD) against selected benchmark's annual return. Benchmarks: 全球 (VWRA), 美國大型 (VUAA), 已開發大型 (VHVE), 新興市場 (VFEM), 台灣 0050.
- Q: TWD performance exchange rate source? → A: Use real historical exchange rates from Stooq API for year-start/year-end valuations (e.g., 2024-12-31 USD/TWD = 32.7897), not hardcoded approximate values.
- Q: Cash flow count display in XIRR card? → A: Show actual transaction count instead of total cash flows (which includes year-start/end valuations). E.g., "1 筆交易" instead of "3 筆現金流".
- Q: "原幣報酬率（不含匯率變動）" text under USD XIRR card? → A: Replace with info icon (ℹ️) tooltip to save space.
- Q: "年底價值" label in yearly summary for YTD? → A: Use dynamic label - "目前價值" for current year (YTD), "年底價值" for historical years.
- Q: Available benchmarks for performance comparison? → A: Display all 11 benchmarks supported by backend: 全球 (VWRA), 美國大型 (VUAA), 美國小型 (XRSU), 已開發大型 (VHVE), 已開發小型 (WSML), 已開發除美 (EXUS), 新興市場 (VFEM), 歐洲 (VEUA), 日本 (VJPA), 中國 (HCHA), 台灣 0050.
- Q: Multiple benchmark selection? → A: Support multi-select for comparing portfolio against multiple benchmarks simultaneously.
- Q: Benchmark loading flicker issue? → A: Maintain previous benchmark value during loading instead of showing 0, to prevent visual flicker.
- Q: Missing price warning timing? → A: Only show "手動輸入" button after auto-fetch actually fails, not immediately when prices are missing.
- Q: Portfolio switching XIRR stale data? → A: Clear XIRR/summary state immediately when switching portfolios to prevent stale data display.
- Q: TWD XIRR card transaction count text "N 筆交易（含匯率變動）"? → A: Replace with Info icon (ℹ️) tooltip to match USD XIRR card style.
- Q: Benchmark selection UI in performance comparison? → A: Use settings gear icon (⚙️) popup instead of inline checkboxes. Read from dashboard's `ytd_benchmark_preferences` localStorage to sync with dashboard YTD section.
- Q: Price fetch loading state visibility? → A: Show skeleton loader or "計算中..." on XIRR cards when fetching prices, instead of displaying "-" which is ambiguous.
- Q: Benchmark chart flickering on page load? → A: Delay rendering performance comparison chart until all selected benchmark data is ready. Show loading state instead of 0 values.
- Q: Which portfolio is used for performance analysis? → A: Currently uses first portfolio only (`portfolios[0]`). Not all portfolios combined. Not synced with portfolio page selection.
- Q: Why cache historical year-end prices? → A: External APIs (Stooq, TWSE) have rate limits; caching prevents repeated API calls for unchanging historical data and improves performance calculation reliability.
- Q: Which data types to cache? → A: Year-end stock prices and year-end exchange rates. Both needed for annual performance calculation.
- Q: Cache scope? → A: Global cache (not per-user). Historical year-end prices are the same for all users.
- Q: When to populate cache? → A: On-demand with lazy loading - when performance calculation needs a price, check cache first, fetch from API if missing, then save to cache.
- Q: How to handle current year (YTD)? → A: Do NOT cache current year data since prices are still changing. Only cache completed years.
- Q: Can users overwrite cached historical prices? → A: No. Cache is global (shared by all users), so cached prices are immutable once saved. Manual entry only fills empty cache entries. Errors require database-level correction.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Optional Exchange Rate for Stock Transactions (Priority: P1)

As an investor, I want to optionally omit the exchange rate when recording stock transactions, so the system displays costs in the stock's native currency instead of forcing conversion to TWD. This resolves the current issue where "total costs always display in TWD and XIRR is affected by input exchange rates."

**Why this priority**: This is a core data entry issue affecting the accuracy of all foreign currency stock transaction records. Must be resolved first.

**Independent Test**: Can be verified by adding a USD-denominated stock transaction without exchange rate; system should correctly display USD cost instead of converting to TWD.

**Acceptance Scenarios**:

1. **Given** user is on the add stock transaction page, **When** selecting a USD-denominated stock and not entering exchange rate, **Then** system accepts the transaction and displays cost in USD
2. **Given** user has a USD transaction without exchange rate, **When** viewing holdings details, **Then** cost displays in USD amount without TWD conversion
3. **Given** user has multiple mixed (with/without exchange rate) transactions for the same stock, **When** calculating average cost, **Then** system calculates separately by source currency without mixing currencies
4. **Given** user has foreign currency transactions without exchange rate, **When** calculating XIRR, **Then** system handles correctly (calculates in source currency or excludes)

---

### User Story 2 - Dashboard Pie Chart for Asset Allocation (Priority: P2)

As an investor, I want to see a pie chart displaying asset allocation on the dashboard, replacing the current presentation, to provide more intuitive visualization of asset proportions.

**Why this priority**: Visualization improvements significantly enhance user experience without affecting core data logic.

**Independent Test**: Can be verified by logging into dashboard and checking if pie chart correctly displays asset category proportions.

**Acceptance Scenarios**:

1. **Given** user logs into dashboard, **When** page finishes loading, **Then** displays pie chart showing asset allocation
2. **Given** user has multiple asset categories, **When** viewing pie chart, **Then** each category is distinguished by different colors and shows percentage
3. **Given** user hovers mouse over pie chart segment, **When** hover event triggers, **Then** displays detailed amount information for that category

---

### User Story 3 - Euronext Exchange Support (Priority: P3)

As an investor, I want the system to support USD-denominated stocks listed on Euronext Amsterdam (Dutch exchange), such as AGAC ETF (IE000FHBZDZ8), to track ETFs purchased on European exchanges.

**Why this priority**: Expanding exchange support is a feature enhancement with no immediate impact on existing users.

**Independent Test**: Can be verified by adding a Euronext stock and verifying real-time quote fetching functionality.

**Acceptance Scenarios**:

1. **Given** user adds a Euronext-listed stock (e.g., AGAC), **When** system fetches real-time quote, **Then** uses Euronext API to obtain correct price
2. **Given** user holds Euronext stocks, **When** viewing holdings details, **Then** displays correct real-time market value
3. **Given** year-end performance calculation, **When** Euronext stock historical closing price is needed, **Then** system can retrieve or allow manual input of year-end price

---

### User Story 4 - Historical Year Performance (Priority: P4)

As an investor, I want to view historical year performance (such as 2023, 2022, etc.), extending beyond the current YTD-only limitation.

**Why this priority**: Performance analysis expansion is an advanced feature that depends on existing YTD infrastructure.

**Independent Test**: Can be verified by selecting a specific year and verifying the accuracy of that year's performance calculation.

**Acceptance Scenarios**:

1. **Given** user is on performance page, **When** selecting "2023" year, **Then** displays 2023 investment performance
2. **Given** user selects historical year performance, **When** page loads, **Then** displays that year's XIRR, total return rate, and other metrics
3. **Given** user switches between different years, **When** year changes, **Then** performance data updates in real-time

---

### User Story 5 - Extended YTD Support for All Stock Types (Priority: P5)

As an investor, I want YTD performance calculation to extend to all stock types, including Taiwan stocks (requiring dividend adjustment) and all accumulating ETFs, rather than being limited to the current 11 designated ETFs.

**Why this priority**: Expanding YTD support scope is an advanced optimization requiring additional data processing logic.

**Independent Test**: Can be verified by viewing YTD performance including Taiwan stocks and verifying dividend adjustment is correctly applied.

**Acceptance Scenarios**:

1. **Given** user holds Taiwan stocks, **When** viewing YTD performance, **Then** performance calculation includes dividend-adjusted data
2. **Given** user holds accumulating ETFs, **When** viewing YTD performance, **Then** correctly displays that ETF's YTD return
3. **Given** system cannot determine if ETF is accumulating type, **When** calculating YTD, **Then** defaults to accumulating (no adjustment) and marks as "unconfirmed type"

---

### User Story 6 - Enhanced Performance Visualization with Bar Charts (Priority: P6)

As an investor, I want performance comparison to use bar charts or other more intuitive methods, replacing the current block-style sorting.

**Why this priority**: Visual optimization is the last enhancement feature to be processed.

**Independent Test**: Can be verified by viewing performance comparison page and checking if bar chart correctly displays performance comparisons.

**Acceptance Scenarios**:

1. **Given** user is on performance comparison page, **When** page loads, **Then** uses bar chart to present performance comparison
2. **Given** bar chart displays multiple performance metrics, **When** viewing chart, **Then** each metric is distinguished by different colors with clear value labels
3. **Given** user hovers over bar chart, **When** interaction triggers, **Then** displays detailed performance data for that item

---

### User Story 7 - Auto-Fetch Price for New Positions (Priority: P1)

As an investor, I want the system to automatically fetch real-time stock prices when I add a transaction for a new stock (new position), so I can immediately see the current market value without manually refreshing.

**Why this priority**: This is a core UX issue affecting new position visibility.

**Independent Test**: Can be verified by adding a new stock transaction and checking if the price is automatically fetched.

**Acceptance Scenarios**:

1. **Given** user adds a Buy transaction for a stock not currently held, **When** transaction is saved, **Then** system automatically fetches real-time price for that stock
2. **Given** user adds a Buy transaction for an existing position, **When** transaction is saved, **Then** system does not re-fetch (uses cached price)
3. **Given** auto-fetch completes, **When** viewing portfolio, **Then** the new position shows current price and unrealized P&L

---

### User Story 8 - Euronext Change Percentage Display (Priority: P2)

As an investor, I want to see the "Since Previous Close" percentage change for Euronext-listed stocks (such as AGAC), so I can quickly understand daily price movement.

**Why this priority**: Completes the Euronext integration with proper display parity.

**Independent Test**: Can be verified by viewing a Euronext stock position and checking if change percentage is displayed.

**Acceptance Scenarios**:

1. **Given** user holds a Euronext stock, **When** viewing position card, **Then** displays change percentage (e.g., "+1.23%" or "-0.45%")
2. **Given** Euronext API returns change data, **When** parsing response, **Then** extracts and stores change percentage
3. **Given** change percentage is positive, **When** displaying, **Then** shows green color; if negative, shows red color

---

### User Story 9 - Foreign Currency Portfolio (No Exchange Rate) (Priority: P1)

As an investor, I want a dedicated portfolio for foreign currency stocks that don't use currency ledger and don't require exchange rate input, so that my performance metrics (XIRR, unrealized P&L) are calculated purely in the stock's source currency without TWD conversion confusion.

**Why this priority**: This resolves the fundamental issue where mixed exchange rate transactions cause confusing/meaningless metrics.

**Independent Test**: Can be verified by creating a foreign currency portfolio, adding transactions without exchange rate, and verifying all metrics are in source currency.

**Acceptance Scenarios**:

1. **Given** user creates a new portfolio, **When** setting portfolio type, **Then** can choose "Foreign Currency Portfolio" option
2. **Given** user is in a Foreign Currency Portfolio, **When** adding transactions, **Then** exchange rate field is hidden/disabled
3. **Given** Foreign Currency Portfolio with USD stocks, **When** viewing metrics, **Then** all costs, values, and P&L display in USD (not TWD)
4. **Given** Foreign Currency Portfolio, **When** calculating XIRR, **Then** XIRR is calculated in source currency cash flows
5. **Given** user has multiple portfolios, **When** on portfolio page, **Then** can switch between portfolios via dropdown/selector
6. **Given** user switches portfolio, **When** selection changes, **Then** all displayed data updates to reflect selected portfolio

---

### User Story 10 - Performance Page UX Improvements (Priority: P2)

As an investor, I want the performance page to have clearer labeling and better interaction patterns, so I can understand the data without confusion and compare my portfolio against multiple benchmarks.

**Why this priority**: These are UX polish items that improve comprehension without affecting core functionality.

**Independent Test**: Can be verified by viewing performance page and checking label clarity, benchmark options, and loading states.

**Acceptance Scenarios**:

1. **Given** user views XIRR TWD card, **When** looking at cash flow info, **Then** shows actual transaction count (e.g., "1 筆交易") instead of total cash flows
2. **Given** user views XIRR USD card, **When** looking at explanation, **Then** shows info icon with tooltip instead of inline text
3. **Given** user views YTD performance (current year), **When** looking at summary, **Then** shows "目前價值" instead of "年底價值"
4. **Given** user views historical year performance, **When** looking at summary, **Then** shows "年底價值"
5. **Given** user opens benchmark dropdown, **When** viewing options, **Then** sees all 11 available benchmarks
6. **Given** user wants to compare multiple benchmarks, **When** selecting benchmarks, **Then** can select multiple benchmarks for simultaneous comparison
7. **Given** user switches benchmark, **When** new data is loading, **Then** previous benchmark bar remains visible (no flicker to 0)
8. **Given** user switches year with missing prices, **When** auto-fetch is in progress, **Then** "手動輸入" button only appears after fetch fails

---

### User Story 11 - Portfolio Switching State Management (Priority: P1)

As an investor, I want the UI to clear stale data when I switch portfolios or create a new portfolio, so I don't see misleading XIRR values from the previous portfolio.

**Why this priority**: This is a data correctness issue that could mislead users.

**Independent Test**: Can be verified by switching portfolios and checking that XIRR clears immediately before new data loads.

**Acceptance Scenarios**:

1. **Given** user is viewing portfolio A with XIRR data, **When** switching to portfolio B, **Then** XIRR displays loading state (not portfolio A's value)
2. **Given** user creates a new empty portfolio, **When** viewing the new portfolio, **Then** XIRR shows "-" or empty state (not previous portfolio's value)
3. **Given** user switches portfolio on Portfolio page, **When** navigating to Performance page, **Then** Performance page shows correct portfolio's data

---

### User Story 12 - Historical Year-End Price Cache (Priority: P1)

As an investor, I want the system to cache historical year-end stock prices and exchange rates, so that performance calculations don't fail due to external API rate limits and load faster on subsequent views.

**Why this priority**: This is a reliability and performance issue - API rate limits cause calculation failures, and repeated API calls for unchanging historical data waste resources.

**Independent Test**: Can be verified by calculating performance for a historical year, then recalculating - second calculation should use cached data without API calls.

**Acceptance Scenarios**:

1. **Given** user calculates 2024 performance for VT, **When** year-end price is not cached, **Then** system fetches from Stooq API and saves to cache
2. **Given** user recalculates 2024 performance for VT, **When** year-end price is already cached, **Then** system uses cached data without API call
3. **Given** user calculates 2024 performance for 0050.TW, **When** year-end price is not cached, **Then** system fetches from TWSE API and saves to cache
4. **Given** user calculates 2024 performance, **When** USD/TWD year-end exchange rate is needed, **Then** system caches the exchange rate for reuse
5. **Given** user calculates current year (YTD) performance, **When** year-end price is requested, **Then** system does NOT cache (prices still changing)
6. **Given** Stooq/TWSE API fails, **When** cache is empty, **Then** system prompts user for manual price entry

---

### Edge Cases

- When user has both with-exchange-rate and without-exchange-rate transactions for the same stock, how to calculate average cost? → **Resolved**: Use separate portfolios - primary portfolio for TWD-tracked investments, foreign currency portfolio for source-currency-only investments
- When Euronext API cannot connect, how to handle quote fetch failure? → **Resolved**: Display cached price marked as "stale"
- When historical year is missing some stocks' year-end closing prices, how to calculate performance? → **Resolved**: Prompt user to manually input
- When ETF type cannot be determined, what is the default behavior? → **Resolved**: Default to accumulating type, mark as "unconfirmed type"
- When user wants to track foreign stocks without TWD conversion, how to handle? → **Resolved**: Create a Foreign Currency Portfolio where all metrics are in source currency
- When external APIs (Stooq/TWSE) hit rate limits during performance calculation, how to handle? → **Resolved**: Use cached year-end prices; cache is populated on first successful fetch
- When the same ticker exists in different markets (e.g., 0050 in TW vs hypothetical 0050 in another market)? → **Resolved**: Cache key includes ticker AND source/market identifier

## Requirements *(mandatory)*

### Functional Requirements

#### Story 1: Optional Exchange Rate
- **FR-001**: System MUST allow stock transaction exchange rate field to be optional (nullable)
- **FR-002**: System MUST display cost in stock's native currency when exchange rate is not provided
- **FR-003**: System MUST track mixed transaction costs separately: with exchange rate calculates in TWD, without exchange rate calculates in source currency, without merging average cost calculations
- **FR-004**: System MUST correctly handle transactions without exchange rate in XIRR calculations

#### Story 2: Dashboard Pie Chart
- **FR-010**: Dashboard MUST display asset allocation using a pie chart
- **FR-011**: Pie chart MUST show percentage for each asset category
- **FR-012**: Pie chart MUST support hover to display detailed information
- **FR-013**: Pie chart MUST classify assets by market/exchange (Taiwan, US, UK, Euronext)

#### Story 3: Euronext Support
- **FR-020**: System MUST support Euronext Amsterdam exchange stocks
- **FR-021**: System MUST use Euronext API (`live.euronext.com/en/ajax/getDetailedQuote/{ISIN}-{MIC}`) to fetch real-time quotes
- **FR-021a**: System MUST display cached price marked as "stale" when Euronext API fails
- **FR-022**: System MUST support ISIN as Euronext stock identifier
- **FR-023**: System SHOULD support retrieving Euronext stock year-end historical closing prices *(deferred: manual input via FR-030a covers this need initially; automated retrieval is a future enhancement)*

#### Story 4: Historical Year Performance
- **FR-030**: System MUST support viewing historical year performance (from 2020 onwards)
- **FR-030a**: System MUST prompt user to manually input when year-end closing price is missing
- **FR-031**: System MUST calculate XIRR and total return rate for specified year
- **FR-032**: System MUST provide year selector for user to switch years
- **FR-033**: Year selector MUST only display years where user has transaction records (dynamic range)

#### Story 5: Extended YTD Support
- **FR-040**: System MUST extend YTD support to all accumulating ETFs
- **FR-041**: System MUST apply dividend adjustment for Taiwan stocks when calculating YTD
- **FR-042**: System SHOULD provide ETF type marking mechanism (accumulating/distributing)
- **FR-043**: System MUST default to accumulating type (no dividend adjustment) when ETF type cannot be determined, and mark as "unconfirmed type"

#### Story 6: Enhanced Visualization
- **FR-050**: Performance comparison page MUST use bar charts
- **FR-051**: Bar chart MUST support comparison display of multiple performance metrics
- **FR-052**: Bar chart MUST support hover interaction to display detailed information

#### Story 7: Auto-Fetch Price for New Positions
- **FR-060**: System MUST automatically fetch real-time price when a new position is created (first transaction for a ticker)
- **FR-061**: System SHOULD NOT re-fetch price for existing positions on transaction add (use cached price)
- **FR-062**: System MUST display current price and unrealized P&L immediately after auto-fetch completes

#### Story 8: Euronext Change Percentage
- **FR-070**: System MUST extract and display "Since Previous Close" percentage change from Euronext API
- **FR-071**: System MUST display change percentage with appropriate color coding (green for positive, red for negative)
- **FR-072**: EuronextQuoteResult MUST include ChangePercent field

#### Story 9: Foreign Currency Portfolio
- **FR-080**: System MUST support creating a "Foreign Currency Portfolio" type
- **FR-081**: Foreign Currency Portfolio MUST NOT require exchange rate input for transactions
- **FR-082**: Foreign Currency Portfolio MUST calculate and display all metrics (cost, value, P&L, XIRR) in source currency
- **FR-083**: System MUST provide portfolio switcher UI to switch between multiple portfolios
- **FR-084**: System MUST update all displayed data when user switches portfolio
- **FR-085**: Each Foreign Currency Portfolio MUST have a single designated Base Currency (e.g., USD)
- **FR-086**: System MUST only allow adding stocks denominated in the portfolio's Base Currency

#### Story 10: Performance Page UX Improvements
- **FR-090**: XIRR TWD card MUST display actual transaction count instead of total cash flow count
- **FR-091**: XIRR USD card MUST use info icon with tooltip instead of inline explanatory text
- **FR-092**: Yearly summary MUST use dynamic label: "目前價值" for current year (YTD), "年底價值" for historical years
- **FR-093**: Benchmark dropdown MUST display all 11 supported benchmarks (VWRA, VUAA, XRSU, VHVE, WSML, EXUS, VFEM, VEUA, VJPA, HCHA, 0050)
- **FR-094**: Benchmark comparison MUST support multi-select for comparing against multiple benchmarks
- **FR-095**: Benchmark bar chart MUST maintain previous value during loading to prevent flicker
- **FR-096**: Missing price "手動輸入" button MUST only appear after auto-fetch fails, not immediately

#### Story 11: Portfolio Switching State Management
- **FR-100**: System MUST clear XIRR and summary state when switching portfolios
- **FR-101**: New empty portfolio MUST display "-" or loading state for XIRR, not stale data
- **FR-102**: Portfolio switch MUST propagate to Performance page correctly

#### Story 12: Historical Year-End Price Cache
- **FR-110**: System MUST cache year-end stock prices to avoid repeated API calls to Stooq/TWSE
- **FR-111**: System MUST cache year-end exchange rates (e.g., USD/TWD on 2024-12-31)
- **FR-112**: Cache MUST use on-demand lazy loading: check cache first, fetch from API if missing, save to cache
- **FR-113**: System MUST NOT cache current year (YTD) data - only completed years
- **FR-114**: Cache MUST be global (not per-user) since historical prices are the same for all users
- **FR-115**: Cache MUST store: ticker/currency pair, year, price/rate, actual trading date, source, fetched timestamp
- **FR-116**: Cache lookup MUST support both stock prices (by ticker + year) and exchange rates (by currency pair + year)
- **FR-117**: System MUST support Taiwan stocks (TWSE source) and international stocks (Stooq source) in the same cache table
- **FR-118**: System SHOULD allow manual price entry when API fetch fails (only for empty cache entries; cannot overwrite existing cached data)

### Key Entities

- **StockTransaction.ExchangeRate**: Modified to nullable decimal to support omitting exchange rate
- **EuronextQuoteCache**: New Euronext quote cache entity using ISIN-MIC format query
- **YearPerformance**: Extended performance calculation to support historical years
- **EtfClassification**: New ETF type marking (accumulating/distributing/unknown)
- **EuronextQuoteResult.ChangePercent**: New field for storing price change percentage
- **Portfolio.PortfolioType**: New field to distinguish Primary (TWD-tracked) vs Foreign Currency portfolios
- **HistoricalYearEndData**: New cache entity for year-end stock prices and exchange rates
  - DataType: StockPrice | ExchangeRate
  - Ticker: Stock ticker or currency pair (e.g., "VT", "0050", "USDTWD")
  - Year: The year (e.g., 2024)
  - Value: Price or exchange rate
  - Currency: Original currency of the price (e.g., "USD", "TWD")
  - ActualDate: The actual trading date the price was recorded
  - Source: "Stooq" | "TWSE" | "Manual"
  - FetchedAt: Timestamp when data was fetched/entered

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: User can complete a foreign currency stock transaction without exchange rate in under 30 seconds
- **SC-002**: Dashboard pie chart renders within 2 seconds after page load
- **SC-003**: Euronext stock quote fetch success rate above 95% *(validated via application logs and error tracking; formal monitoring deferred to operational phase)*
- **SC-004**: Historical year performance calculation results have less than 0.1% error compared to manual verification
- **SC-005**: All held stocks are included in YTD performance calculation (Taiwan stocks with dividend adjustment)
- **SC-006**: Performance comparison bar chart correctly displays all comparison items without visual misalignment
- **SC-007**: New position displays current price within 3 seconds after transaction is saved
- **SC-008**: Euronext stocks display change percentage with correct color coding
- **SC-009**: Foreign Currency Portfolio displays all metrics in source currency without TWD conversion
- **SC-010**: XIRR card shows transaction count (not cash flow count) for clarity
- **SC-011**: All 11 benchmarks are available in performance comparison dropdown
- **SC-012**: Benchmark switching shows no visual flicker (maintains previous value during loading)
- **SC-013**: Portfolio switching clears stale XIRR data within 100ms
- **SC-014**: Cached year-end prices are reused on subsequent performance calculations (no duplicate API calls for same ticker/year)
- **SC-015**: Performance page loads within 2 seconds when all required prices are cached
- **SC-016**: Cache correctly distinguishes between different data sources (Stooq vs TWSE) for the same ticker pattern

## Assumptions

- Euronext API is publicly available without additional authentication
- Historical year-end closing prices can be obtained through existing HistoricalPrice mechanism or manual input
- Frontend uses Recharts as charting library (consistent with existing tech stack)
- Taiwan stock dividend data can be obtained from existing data sources
- Currently only Taiwan stocks support dividend adjustment; other stocks should use accumulating ETFs for accurate data
- Historical year-end prices are immutable once the year is complete - no need for cache invalidation
- Stooq and TWSE APIs have similar rate limit constraints that necessitate caching
