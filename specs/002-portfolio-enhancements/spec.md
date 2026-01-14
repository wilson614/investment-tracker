# Feature Specification: Portfolio Enhancements V2

**Feature Branch**: `002-portfolio-enhancements`
**Created**: 2026-01-14
**Status**: Draft
**Input**: 6 portfolio enhancement features

## Overview

This module contains 6 enhancements to the existing investment portfolio tracking system, covering transaction flexibility, visualization improvements, exchange support expansion, and performance tracking capabilities.

## Clarifications

### Session 2026-01-14

- Q: How to calculate average cost for mixed exchange rate transactions? → A: Track separately: transactions with exchange rate calculate in TWD, transactions without exchange rate calculate in source currency, do not merge
- Q: How to handle Euronext API failures? → A: Display last successfully fetched cached price, marked as "stale"
- Q: How to handle missing year-end closing prices for historical years? → A: Prompt user to manually input missing year-end closing prices
- Q: Default behavior when ETF type cannot be determined? → A: Default to accumulating type (no dividend adjustment), marked as "unconfirmed type". Note: Currently only Taiwan stocks support dividend adjustment; others should use accumulating ETFs for accurate data; future consideration to add US stock dividend adjustment or fetch reliable sources for annual total return

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

### Edge Cases

- When user has both with-exchange-rate and without-exchange-rate transactions for the same stock, how to calculate average cost? → **Resolved**: Track separately, do not merge
- When Euronext API cannot connect, how to handle quote fetch failure? → **Resolved**: Display cached price marked as "stale"
- When historical year is missing some stocks' year-end closing prices, how to calculate performance? → **Resolved**: Prompt user to manually input
- When ETF type cannot be determined, what is the default behavior? → **Resolved**: Default to accumulating type, mark as "unconfirmed type"

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

#### Story 5: Extended YTD Support
- **FR-040**: System MUST extend YTD support to all accumulating ETFs
- **FR-041**: System MUST apply dividend adjustment for Taiwan stocks when calculating YTD
- **FR-042**: System SHOULD provide ETF type marking mechanism (accumulating/distributing)
- **FR-043**: System MUST default to accumulating type (no dividend adjustment) when ETF type cannot be determined, and mark as "unconfirmed type"

#### Story 6: Enhanced Visualization
- **FR-050**: Performance comparison page MUST use bar charts
- **FR-051**: Bar chart MUST support comparison display of multiple performance metrics
- **FR-052**: Bar chart MUST support hover interaction to display detailed information

### Key Entities

- **StockTransaction.ExchangeRate**: Modified to nullable decimal to support omitting exchange rate
- **EuronextQuoteCache**: New Euronext quote cache entity using ISIN-MIC format query
- **YearPerformance**: Extended performance calculation to support historical years
- **EtfClassification**: New ETF type marking (accumulating/distributing/unknown)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: User can complete a foreign currency stock transaction without exchange rate in under 30 seconds
- **SC-002**: Dashboard pie chart renders within 2 seconds after page load
- **SC-003**: Euronext stock quote fetch success rate above 95% *(validated via application logs and error tracking; formal monitoring deferred to operational phase)*
- **SC-004**: Historical year performance calculation results have less than 0.1% error compared to manual verification
- **SC-005**: All held stocks are included in YTD performance calculation (Taiwan stocks with dividend adjustment)
- **SC-006**: Performance comparison bar chart correctly displays all comparison items without visual misalignment

## Assumptions

- Euronext API is publicly available without additional authentication
- Historical year-end closing prices can be obtained through existing HistoricalPrice mechanism or manual input
- Frontend uses Recharts as charting library (consistent with existing tech stack)
- Taiwan stock dividend data can be obtained from existing data sources
- Currently only Taiwan stocks support dividend adjustment; other stocks should use accumulating ETFs for accurate data
