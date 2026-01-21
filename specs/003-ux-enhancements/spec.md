# Feature Specification: UX Enhancements & Market Selection

**Feature Branch**: `003-ux-enhancements`
**Created**: 2026-01-21
**Status**: Implemented (Phase 1-10 Complete), Phase 2 In Progress (US9-US17)
**Input**: Stock split UI, Benchmark custom stocks, default login page, dashboard historical chart, Taiwan timezone, date input optimization, fee default value, transaction Market field, **[NEW]** transaction currency, XIRR for current year, logout cache cleanup, dashboard layout stability, multi-market same-ticker support, quote fallback logic, ticker prediction trigger, CSV import market/currency, Yahoo historical price fallback

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

---

## Phase 2: New User Stories (US9-US17)

### User Story 9 - Transaction Currency Field (Priority: P1)

Users can input and edit the denomination currency (計價幣別) for each transaction. Auto-detection: Taiwan stocks default to TWD, others default to USD. Users can manually override.

**Why this priority**: Currency is fundamental to accurate cost and performance calculations across multiple markets.

**Independent Test**: Create a transaction for Taiwan stock, verify currency defaults to TWD. Create a US stock transaction, verify currency defaults to USD.

**Acceptance Scenarios**:

1. **Given** user enters Taiwan stock ticker (e.g., 2330), **When** form auto-detects, **Then** currency field defaults to TWD
2. **Given** user enters non-Taiwan stock ticker (e.g., VTI), **When** form auto-detects, **Then** currency field defaults to USD
3. **Given** user manually selects GBP for UK stock, **When** transaction is saved, **Then** currency persists as GBP
4. **Given** user edits existing transaction, **When** form loads, **Then** currency field shows stored value

---

### User Story 10 - XIRR Current Year Handling (Priority: P2)

Current year (2026) XIRR calculation may be misleading at the start of the year due to extreme amplification. System should provide appropriate handling or warning.

**Why this priority**: Misleading XIRR values can cause user confusion and poor decision-making.

**Independent Test**: View XIRR for portfolio with transactions only in current year, verify appropriate handling.

**Acceptance Scenarios**:

1. **Given** portfolio only has current year transactions, **When** viewing XIRR, **Then** system displays warning about limited data reliability
2. **Given** XIRR calculation period is less than 3 months, **When** displaying result, **Then** add visual indicator that value may be volatile
3. **Given** user hovers over warning, **When** tooltip appears, **Then** explain why short-term XIRR can be misleading

---

### User Story 11 - Logout Cache Cleanup (Priority: P1)

After logout, all user-specific cached data must be cleared to prevent data leakage when switching accounts. Some preferences should be stored in database rather than localStorage.

**Why this priority**: Security concern - cached data from previous user should not be visible to next user.

**Independent Test**: Login as User A, view portfolio data. Logout. Login as User B, verify no User A data is visible.

**Acceptance Scenarios**:

1. **Given** user logs out, **When** logout completes, **Then** all localStorage quote caches are cleared
2. **Given** user logs out, **When** logout completes, **Then** all user-specific React Query caches are invalidated
3. **Given** user preferences (e.g., selected portfolio, benchmark settings), **When** stored, **Then** save to database rather than localStorage
4. **Given** new user logs in after logout, **When** viewing dashboard, **Then** no previous user's cached prices or preferences are shown

---

### User Story 12 - Dashboard Layout Stability (Priority: P2)

Dashboard sections should maintain consistent height during loading to prevent layout shifts. This includes the historical chart section (should not hide when no data) and CAPE section.

**Why this priority**: Layout shifts during loading create poor user experience and visual instability.

**Independent Test**: View Dashboard with slow network, verify sections don't jump around during load.

**Acceptance Scenarios**:

1. **Given** historical chart section has no data, **When** Dashboard loads, **Then** section displays placeholder with same height as data state
2. **Given** CAPE section is loading, **When** initial render, **Then** section height matches post-load height
3. **Given** any dashboard section is loading, **When** data arrives, **Then** no visible layout shift occurs

---

### User Story 13 - Multi-Market Same-Ticker Support (Priority: P1)

When user holds same ticker in different markets (e.g., WSML.L in UK and WSML in US), system must treat them as separate positions and display correct market for each.

**Why this priority**: Core data integrity issue - conflating different securities leads to incorrect calculations.

**Independent Test**: Create transactions for WSML (US) and WSML.L (UK), verify they appear as separate positions with correct markets.

**Acceptance Scenarios**:

1. **Given** user has WSML transactions with market=US, **When** viewing positions, **Then** displays as US market position
2. **Given** user has WSML.L transactions with market=UK, **When** viewing positions, **Then** displays as UK market position (separate from US)
3. **Given** same base ticker in different markets, **When** calculating totals, **Then** calculate as separate positions with separate quotes

---

### User Story 14 - Quote Fetching Market Enforcement (Priority: P1)

Position card quote fetching must strictly use the market from transaction records. No fallback to other markets for position cards. Only ticker input prediction during form entry may use market fallback.

**Why this priority**: Fallback logic causes incorrect quotes when user has intentionally set a specific market.

**Independent Test**: Create UK market transaction, verify quote is fetched from UK API only (no US fallback).

**Acceptance Scenarios**:

1. **Given** position has market=UK in transactions, **When** fetching quote, **Then** only use UK market API (no fallback)
2. **Given** UK market API fails to return quote, **When** position card displays, **Then** show error/no quote instead of wrong US quote
3. **Given** user is typing ticker in transaction form, **When** system auto-detects market, **Then** may try fallback (US→UK) for detection only
4. **Given** position card quote fetch fails, **When** displaying, **Then** use last cached value if available, otherwise show "無報價"

---

### User Story 15 - Ticker Prediction Trigger Optimization (Priority: P3)

Ticker input prediction should trigger after 4th character is typed, not only when moving to next field.

**Why this priority**: Improves responsiveness and user experience during data entry.

**Independent Test**: Type 4 characters in ticker field, verify prediction triggers immediately without leaving field.

**Acceptance Scenarios**:

1. **Given** user types "VWRA" in ticker field, **When** 4th character entered, **Then** market detection triggers immediately
2. **Given** user types "VT" (only 2 chars), **When** typing stops, **Then** no prediction triggered yet
3. **Given** user types Taiwan stock "2330", **When** 4th character entered, **Then** prediction triggers (TW market detected)
4. **Given** prediction is running, **When** user continues typing, **Then** previous prediction is cancelled and new one starts

---

### User Story 16 - CSV Import Market and Currency Fields (Priority: P2)

CSV import must include Market (required) and Currency (required) columns for proper transaction import.

**Why this priority**: Without market and currency, imported transactions cannot be properly processed.

**Independent Test**: Import CSV with market and currency columns, verify all values are correctly stored.

**Acceptance Scenarios**:

1. **Given** CSV with Market column, **When** importing, **Then** market values are correctly mapped (TW/US/UK/EU)
2. **Given** CSV with Currency column, **When** importing, **Then** currency values are correctly stored (TWD/USD/GBP/EUR)
3. **Given** CSV missing Market column, **When** attempting import, **Then** validation error displayed
4. **Given** CSV missing Currency column, **When** attempting import, **Then** validation error displayed
5. **Given** CSV template download, **When** user downloads, **Then** template includes Market and Currency columns with examples

---

### User Story 17 - Yahoo Historical Price Fallback (Priority: P2)

Implement Yahoo Finance as fallback for historical price fetching when Stooq is unavailable or rate-limited. Evaluate whether Yahoo should become primary source.

**Why this priority**: Stooq has rate limits and occasional availability issues; having fallback improves reliability.

**Independent Test**: Block Stooq API, verify system successfully fetches historical prices from Yahoo.

**Acceptance Scenarios**:

1. **Given** Stooq returns rate limit error, **When** fetching historical price, **Then** automatically fallback to Yahoo
2. **Given** Stooq timeout or error, **When** fetching, **Then** retry with Yahoo as fallback
3. **Given** Yahoo is configured as primary, **When** Yahoo fails, **Then** fallback to Stooq
4. **Given** both sources fail, **When** displaying, **Then** show appropriate error message

---

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

#### [NEW] Transaction Currency (US9)
- **FR-322**: System MUST add Currency field to StockTransaction table
- **FR-323**: Transaction form MUST include currency dropdown (TWD/USD/GBP/EUR)
- **FR-324**: System MUST auto-detect currency: Taiwan stocks → TWD, all others → USD (user can manually override to GBP/EUR)
- **FR-325**: User MUST be able to manually override detected currency
- **FR-326**: Database migration MUST populate existing transactions based on market

#### [NEW] XIRR Current Year Handling (US10)
- **FR-327**: System MUST display warning when XIRR calculation period < 3 months
- **FR-328**: Short-period XIRR MUST have visual indicator of volatility
- **FR-329**: Warning tooltip MUST explain short-term XIRR limitations

#### [NEW] Logout Cache Cleanup (US11)
- **FR-330**: Logout MUST clear all localStorage keys with `user_` prefix (quote caches, user-specific state)
- **FR-331**: Logout MUST invalidate all React Query caches
- **FR-332**: Chart display preferences (ytd_prefs, cape_region_prefs) MUST be stored in database for cross-device sync
- **FR-333**: localStorage keys MUST use `user_` prefix for user-specific data; shared settings (theme) use different prefix or no prefix
- **FR-333a**: Remove `selected_portfolio_id`/`default_portfolio_id` from localStorage - single portfolio per user, always fetch from API

#### [NEW] Dashboard Layout Stability (US12)
- **FR-334**: Historical chart section MUST maintain consistent height (show placeholder when empty)
- **FR-335**: CAPE section MUST have fixed minimum height during loading
- **FR-336**: All dashboard sections MUST avoid layout shift on data load

#### [NEW] Multi-Market Same-Ticker Support (US13)
- **FR-337**: Position grouping MUST use (ticker, market) composite key, not ticker alone
- **FR-338**: Same ticker in different markets MUST be treated as separate positions
- **FR-339**: Quote fetching MUST use position's specific market

#### [NEW] Quote Fetching Market Enforcement (US14)
- **FR-340**: Position card quote fetching MUST NOT fallback to other markets
- **FR-341**: Quote fetch failure MUST display "無報價" or cached value
- **FR-342**: Ticker input prediction MAY use market fallback for detection
- **FR-343**: Fallback behavior MUST be clearly separated (prediction vs display)

#### [NEW] Ticker Prediction Trigger (US15)
- **FR-344**: Market detection MUST trigger on 4th character typed
- **FR-345**: Previous detection request MUST be cancelled when new input arrives
- **FR-346**: Loading indicator MUST be shown during detection

#### [NEW] CSV Import Market and Currency (US16)
- **FR-347**: CSV import MUST require Market column
- **FR-348**: CSV import MUST require Currency column
- **FR-349**: Validation MUST fail if Market or Currency columns are missing
- **FR-350**: CSV template MUST include Market and Currency columns with examples

#### [NEW] Yahoo Historical Price Fallback (US17)
- **FR-351**: System MUST implement Yahoo Finance as PRIMARY historical price source
- **FR-352**: Yahoo failure MUST trigger Stooq fallback automatically
- **FR-353**: Stooq serves as secondary/fallback source only
- **FR-354**: Both sources failing MUST display appropriate error message

### Key Entities

- **StockTransaction.Market**: Market type for transaction (TW/US/UK/EU), new field
- **StockSplit**: Stock split record containing ticker, split date, split ratio (shared across all users)
- **UserBenchmark**: User's custom Benchmark stocks containing user ID, ticker, market, added date (per-user storage)
- **PortfolioValueHistory**: Portfolio year-end value snapshots for line chart display
- **[NEW] StockTransaction.Currency**: Denomination currency for transaction (TWD/USD/GBP/EUR)
- **[NEW] UserPreferences**: User-specific preferences stored in database (selected portfolio, settings)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-301**: Users can complete transaction market selection within 5 seconds, selection persists after page refresh
- **SC-302**: Benchmark custom stock quote fetching success rate above 95%
- **SC-303**: Dashboard historical chart loads within 3 seconds
- **SC-304**: Stock split record changes trigger position recalculation within 2 seconds
- **SC-305**: 100% of time displays use Taiwan timezone
- **SC-306**: Date input efficiency improved, users can complete full date input within 5 seconds
- **[NEW] SC-307**: Currency selection persists correctly after page refresh
- **[NEW] SC-308**: XIRR warning appears for portfolios with < 3 months history
- **[NEW] SC-309**: Zero user-specific data leakage after logout (verified by cache inspection)
- **[NEW] SC-310**: Dashboard layout shift < 5px during loading
- **[NEW] SC-311**: Same-ticker different-market positions display as separate entries
- **[NEW] SC-312**: Position card never shows quote from wrong market
- **[NEW] SC-313**: Ticker prediction triggers within 100ms of 4th character
- **[NEW] SC-314**: CSV import validates Market/Currency columns before processing
- **[NEW] SC-315**: Historical price fallback success rate > 99% when primary source fails

## Clarifications

### Session 2026-01-21

- Q: What data granularity for historical value chart? → A: Year-end snapshots (1 point per year), reuse existing year-end value data, can expand to monthly later
- Q: How to initialize Market field for existing transactions? → A: Migration auto-populates based on ticker format, users can edit transactions to correct
- Q: Where should Stock Split UI be located? → A: Sub-section in Settings page
- Q: Should Benchmark custom stocks be per-user or global? → A: Per-user storage (personalized Benchmark), historical prices shared, real-time prices fetched on demand
- Q: Yahoo/Stooq historical price source priority? → A: Yahoo as primary source, Stooq as fallback (prioritizing stability)
- Q: Default currency for UK/EU stocks? → A: All non-Taiwan stocks default to USD; user can manually change to GBP/EUR
- Q: localStorage cleanup strategy on logout? → A: Use `user_` prefix for user-specific keys, clear only these on logout; critical preferences (selected portfolio) should be stored in DB
- Q: Portfolio selection mechanism needed? → A: No - spec defines single portfolio per user (FR-020/FR-021); remove `selected_portfolio_id`/`default_portfolio_id` from localStorage, always fetch from API
- Q: Chart display preferences (ytd_prefs, cape_region_prefs) storage? → A: Store in database for cross-device sync

## Assumptions

1. Market selection uses latest transaction's setting for quote fetching
2. Stock split data is shared, no user permission differentiation (all users can edit)
3. Historical value chart uses year-end snapshot data, not daily data
4. Benchmark custom stocks have no quantity limit, but recommend max 5 for chart readability
5. Timezone adjustment only affects frontend display, backend still stores UTC
