# Feature Specification: UX Enhancements & Market Selection

**Feature Branch**: `003-ux-enhancements`
**Created**: 2026-01-21
**Status**: Implemented (Phase 1-10 Complete)
**Input**: Stock split UI, Benchmark custom stocks, default login page, dashboard historical chart, Taiwan timezone, date input optimization, fee default value, transaction Market field

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Transaction Market Selection (Priority: P1)

When creating a transaction, the system auto-predicts the market (TW/US/UK) based on ticker format, but users can manually override. The selected market is persisted in the database and used for subsequent price fetching.

**Why this priority**: Core improvement that solves market selection persistence issue, directly affecting price fetching accuracy.

**Independent Test**: Create a transaction with manual market selection, refresh the page, verify market setting persists and correct market API is used.

**Acceptance Scenarios**:

1. **Given** user enters ticker "WSML" in transaction form, **When** system predicts US market, **Then** user can manually select UK from dropdown
2. **Given** user selects UK market and saves transaction, **When** page refreshes, **Then** position displays UK market
3. **Given** position market is set to UK, **When** system fetches quote, **Then** UK API is used instead of US API
4. **Given** user views position card, **When** position has multiple transactions, **Then** display market from latest transaction (read-only)

---

### User Story 2 - Benchmark Custom Stocks (Priority: P1)

Users can add custom stocks as benchmark comparisons, supporting Taiwan and international markets. System uses Sina (real-time) and Stooq (historical) APIs. For non-accumulating international ETFs, system displays dividend impact warning.

**Why this priority**: Benchmark is critical for performance evaluation; custom stocks enable meaningful comparisons.

**Independent Test**: Add a custom stock benchmark, verify system fetches quotes and displays in benchmark chart.

**Acceptance Scenarios**:

1. **Given** user in Benchmark settings, **When** enters Taiwan stock code (e.g., 0050), **Then** system fetches via Sina/Stooq and adds to benchmark list
2. **Given** user enters international stock code (e.g., VTI), **When** stock is non-accumulating ETF, **Then** system displays warning "Non-accumulating international ETFs are affected by dividends, return comparison may be biased"
3. **Given** benchmark list contains custom stocks, **When** user views Performance page, **Then** custom stock historical performance curves display alongside portfolio

---

### User Story 3 - Dashboard Historical Value Chart (Priority: P2)

Users can view a line chart showing portfolio value changes over years on the Dashboard, presenting long-term growth trends.

**Why this priority**: Visual historical performance helps users understand long-term investment results.

**Independent Test**: View Dashboard and verify line chart displays correctly, matching backend year-end value data.

**Acceptance Scenarios**:

1. **Given** user has over one year of transactions, **When** entering Dashboard, **Then** display historical value line chart with time on X-axis, home currency value on Y-axis
2. **Given** user hovers over a point on chart, **When** point corresponds to specific date, **Then** display value data for that date
3. **Given** user has no transactions, **When** entering Dashboard, **Then** chart area displays "No historical data"

---

### User Story 4 - Stock Split Settings UI (Priority: P2)

Users can maintain stock split records through a sub-section in Settings page, including create, edit, and delete operations. This is shared data maintained by all users.

**Why this priority**: Stock splits affect share count and cost calculations, requiring convenient maintenance.

**Independent Test**: Add a split record, verify share count adjusts correctly after split date.

**Acceptance Scenarios**:

1. **Given** user enters Stock Split settings page, **When** clicks Add button, **Then** form displays for ticker, split date, split ratio
2. **Given** user enters split ratio 2:1, **When** saved, **Then** share count after split date automatically doubles
3. **Given** split record exists, **When** user edits or deletes record, **Then** related position calculations update automatically

---

### User Story 5 - Default to Dashboard After Login (Priority: P3)

After login, users are redirected to Dashboard instead of Portfolio page.

**Why this priority**: Dashboard provides overall summary, more suitable as default landing page.

**Independent Test**: Login and verify automatic redirect to Dashboard.

**Acceptance Scenarios**:

1. **Given** user successfully logs in, **When** login completes, **Then** automatically redirect to Dashboard
2. **Given** user is logged in and on another page, **When** browser refreshes, **Then** stay on current page (no redirect)

---

### User Story 6 - Taiwan Timezone Display (Priority: P3)

All time displays in the system use Taiwan time (UTC+8), including transaction dates and update times.

**Why this priority**: Users are in Taiwan; local time is more intuitive.

**Independent Test**: View transaction record times and verify Taiwan timezone is used.

**Acceptance Scenarios**:

1. **Given** backend stores UTC time, **When** frontend displays time, **Then** automatically convert to Taiwan time (UTC+8)
2. **Given** user creates transaction and selects date, **When** saved and viewed again, **Then** displayed date matches input (no timezone offset)

---

### User Story 7 - Date Input Auto-Tab Optimization (Priority: P3)

In date input fields, after entering 4-digit year, cursor automatically moves to month field, consistent with current behavior of month input moving to day field.

**Why this priority**: Improves data entry efficiency and user experience.

**Independent Test**: Enter "2024" in year field, verify cursor automatically moves to month field.

**Acceptance Scenarios**:

1. **Given** user in year field enters "2024", **When** fourth digit entered, **Then** cursor automatically moves to month field
2. **Given** user in month field enters "02", **When** second digit entered, **Then** cursor automatically moves to day field (maintain current behavior)
3. **Given** user in year field enters "202", **When** only three digits entered, **Then** cursor stays, waiting for fourth digit

---

### User Story 8 - Fee Field Default Value Adjustment (Priority: P3)

Transaction form fee field defaults to empty instead of auto-filling 0.

**Why this priority**: Prevents users from assuming fee is already filled and skipping.

**Independent Test**: Open new transaction form, verify fee field is empty.

**Acceptance Scenarios**:

1. **Given** user opens new transaction form, **When** form loads, **Then** fee field is empty (not 0)
2. **Given** fee field is empty, **When** user submits form, **Then** system treats empty as 0
3. **Given** user edits existing transaction, **When** original fee was 0, **Then** field displays 0 (not empty)

---

### Edge Cases

- When selected market API cannot fetch quote, system should try other markets as fallback
- When stock split record date is before user's first transaction, should handle correctly
- When Benchmark custom stock stops trading, should display last available quote
- When non-numeric characters entered in year field, should not trigger auto-tab

## Requirements *(mandatory)*

### Functional Requirements

#### Transaction Market Selection
- **FR-301**: System MUST add Market field to StockTransaction table, storing market type
- **FR-302**: System MUST provide market selection dropdown in transaction form (TW/US/UK/EU)
- **FR-303**: System MUST auto-predict market based on ticker format as default option
- **FR-304**: Position card MUST display market label (read-only), no selection functionality
- **FR-305**: Quote fetching MUST prioritize market setting from transaction record over guessing
- **FR-306**: Database migration MUST auto-populate existing transactions' Market field based on ticker format

#### Benchmark Custom Stocks
- **FR-307**: System MUST support user adding custom stocks as Benchmark (per-user storage)
- **FR-308**: System MUST support Taiwan stock quotes (real-time Sina, historical Stooq)
- **FR-309**: System MUST support international stock quotes (real-time Sina, historical Stooq)
- **FR-310**: System MUST display dividend impact warning next to non-accumulating international ETFs

#### Dashboard Historical Chart
- **FR-311**: Dashboard MUST display historical value line chart (year-end snapshots)
- **FR-312**: Line chart MUST support hover to display detailed data

#### Stock Split Settings
- **FR-313**: System MUST provide CRUD UI for stock split records in Settings page
- **FR-314**: Stock split data MUST be shared maintenance (all users share same data)
- **FR-315**: Split record changes MUST automatically trigger position recalculation

#### Default Login Page
- **FR-316**: User MUST be redirected to Dashboard after login

#### Time Display
- **FR-317**: Frontend time display MUST use Taiwan timezone (UTC+8)

#### Date Input Optimization
- **FR-318**: Year input of 4 digits MUST auto-tab to month field
- **FR-319**: Month input of 2 digits MUST auto-tab to day field (maintain current)

#### Fee Default Value
- **FR-320**: New transaction form fee field MUST default to empty
- **FR-321**: Empty fee MUST be treated as 0 on submission

### Key Entities

- **StockTransaction.Market**: Market type for transaction (TW/US/UK/EU), new field
- **StockSplit**: Stock split record containing ticker, split date, split ratio (shared across all users)
- **UserBenchmark**: User's custom Benchmark stocks containing user ID, ticker, market, added date (per-user storage)
- **PortfolioValueHistory**: Portfolio year-end value snapshots for line chart display

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-301**: Users can complete transaction market selection within 5 seconds, selection persists after page refresh
- **SC-302**: Benchmark custom stock quote fetching success rate above 95%
- **SC-303**: Dashboard historical chart loads within 3 seconds
- **SC-304**: Stock split record changes trigger position recalculation within 2 seconds
- **SC-305**: 100% of time displays use Taiwan timezone
- **SC-306**: Date input efficiency improved, users can complete full date input within 5 seconds

## Clarifications

### Session 2026-01-21

- Q: What data granularity for historical value chart? → A: Year-end snapshots (1 point per year), reuse existing year-end value data, can expand to monthly later
- Q: How to initialize Market field for existing transactions? → A: Migration auto-populates based on ticker format, users can edit transactions to correct
- Q: Where should Stock Split UI be located? → A: Sub-section in Settings page
- Q: Should Benchmark custom stocks be per-user or global? → A: Per-user storage (personalized Benchmark), historical prices shared, real-time prices fetched on demand

## Assumptions

1. Market selection uses latest transaction's setting for quote fetching
2. Stock split data is shared, no user permission differentiation (all users can edit)
3. Historical value chart uses year-end snapshot data, not daily data
4. Benchmark custom stocks have no quantity limit, but recommend max 5 for chart readability
5. Timezone adjustment only affects frontend display, backend still stores UTC
