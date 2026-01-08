# Feature Specification: Family Investment Portfolio Tracker

**Feature Branch**: `001-portfolio-tracker`
**Created**: 2026-01-06
**Updated**: 2026-01-08
**Status**: Implementation Complete
**Input**: User description: "Build a Family Investment Portfolio Tracker to replace a manual spreadsheet system with multi-tenancy support"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record Stock Purchase (Priority: P1)

As a family member, I want to record a stock/ETF purchase transaction so that I can track my investment cost basis accurately.

**Why this priority**: This is the foundational capability. Without transaction recording, no other features (performance metrics, PnL calculations) can function. This represents the core value proposition of replacing the spreadsheet.

**Independent Test**: Can be fully tested by entering a buy transaction with all required fields (date, ticker, shares, price, exchange rate, fees) and verifying the transaction appears in the portfolio with correct cost calculations.

**Acceptance Scenarios**:

1. **Given** I am logged in as a family member, **When** I enter a buy transaction for 10.5 shares of VWRA at $120.50 USD with exchange rate 31.5 TWD/USD and $5 fees, **Then** the system records the transaction and calculates Total Cost (Source) = (10.5 × 120.50) + 5 = $1,270.25 USD and Total Cost (Home) = 1,270.25 × 31.5 = 40,012.88 TWD.

2. **Given** I have an existing position in VTI, **When** I record another buy transaction for the same ticker, **Then** the system updates my total shares and recalculates the moving average cost per share.

3. **Given** I am entering a transaction, **When** I enter fractional shares up to 4 decimal places (e.g., 6.8267), **Then** the system accepts and stores the exact value without rounding.

---

### User Story 2 - Manage Foreign Currency Ledger (Priority: P2)

As a family member, I want to track my foreign currency (USD) holdings with weighted average cost so that I know my true cost basis for currency and can calculate FX gains/losses.

**Why this priority**: Currency ledger is essential for accurate cost basis calculation when the home currency differs from investment currency. It enables proper FX gain/loss tracking.

**Independent Test**: Can be fully tested by recording currency exchange transactions and verifying the weighted average rate updates correctly.

**Acceptance Scenarios**:

1. **Given** I have 0 USD balance, **When** I exchange 100,000 TWD to 3,200 USD at rate 31.25, **Then** my USD balance becomes 3,200 and weighted average rate is 31.25.

2. **Given** I have 3,200 USD at weighted average rate 31.25, **When** I exchange another 62,000 TWD to 2,000 USD at rate 31.00, **Then** my new weighted average rate = ((3,200 × 31.25) + (2,000 × 31.00)) / (3,200 + 2,000) = 31.154.

3. **Given** I have 5,000 USD at weighted average rate 31.15, **When** I sell 1,000 USD at rate 32.00, **Then** I realize FX profit = 1,000 × (32.00 - 31.15) = 850 TWD and my USD balance becomes 4,000 (weighted average rate unchanged).

4. **Given** I receive bank interest of 50 USD, **When** I record an Interest transaction, **Then** my USD balance increases by 50 and weighted average rate decreases (cost basis 0 for interest).

---

### User Story 3 - Buy Stock Using Currency Ledger (Priority: P3)

As a family member, I want to link my stock purchases to my currency ledger so that the system automatically deducts foreign currency when I buy stocks.

**Why this priority**: This integration eliminates manual reconciliation between stock transactions and currency holdings, reducing errors and saving time.

**Independent Test**: Can be fully tested by purchasing a stock and selecting "Currency Ledger" as fund source, then verifying the currency balance decreases by the correct amount.

**Acceptance Scenarios**:

1. **Given** I have 5,000 USD in my currency ledger at weighted average rate 31.15, **When** I buy 10 shares of VTI at $200 + $5 fees using Currency Ledger as fund source, **Then** the system creates a Spend transaction for $2,005 USD, my currency balance becomes 2,995 USD, and the weighted average rate remains 31.15.

2. **Given** I have insufficient USD balance (1,000 USD), **When** I attempt to buy stocks costing 2,000 USD using Currency Ledger, **Then** the system displays an error indicating insufficient funds.

---

### User Story 4 - View Portfolio Performance with Real-time Quotes (Priority: P4)

As a family member, I want to see my portfolio performance metrics (XIRR, Unrealized PnL, Average Cost) with real-time stock prices so that I can evaluate my investment returns.

**Why this priority**: Performance metrics provide actionable insights but require the foundational transaction data from P1-P3 to be meaningful.

**Independent Test**: Can be fully tested by recording several transactions, fetching current quotes, and verifying calculated metrics match expected values based on known formulas.

**Acceptance Scenarios**:

1. **Given** I have multiple buy transactions for VWRA over time, **When** I view the portfolio, **Then** I see the moving average cost per share in source currency (USD).

2. **Given** I click "獲取報價" for a position, **When** the quote is fetched successfully, **Then** the system displays current price, calculates unrealized PnL, and shows visual feedback ("已更新" for success, "無法取得報價" for error).

3. **Given** current market price for VWRA is $130 USD and exchange rate is 32.00, **When** I view unrealized PnL, **Then** the system calculates: Unrealized PnL (Home) = (Current Price × Total Shares × Current FX Rate) - Total Cost (Home Currency).

4. **Given** I click "獲取全部報價" button, **When** the system fetches quotes for all positions, **Then** all positions update with current values and XIRR is calculated based on cash flows and current market value.

5. **Given** I have recently fetched quotes (within 1 hour), **When** I return to the Dashboard, **Then** the system automatically loads cached prices and displays approximate metrics without requiring manual refresh.

---

### User Story 5 - Sell Stock and Calculate Realized PnL (Priority: P5)

As a family member, I want to sell stocks and see the realized profit/loss in my home currency so that I can understand my actual investment returns.

**Why this priority**: Realized PnL completes the investment lifecycle but depends on accurate cost basis tracking from earlier stories.

**Independent Test**: Can be fully tested by selling shares and verifying the realized PnL calculation matches expected values.

**Acceptance Scenarios**:

1. **Given** I own 20 shares of VTI with average cost 38,000 TWD (Home), **When** I sell 10 shares at $210 USD with exchange rate 32.00, **Then** Realized Amount (Home) = 10 × 210 × 32.00 = 67,200 TWD, Cost Basis (Home) = 10 × (38,000/20) = 19,000 TWD, Realized Profit = 67,200 - 19,000 = 48,200 TWD.

2. **Given** a completed sale transaction, **When** I view the transaction, **Then** I see Return Rate % = (Realized Profit / Cost Basis) × 100.

---

### User Story 6 - Multi-Tenancy and Data Isolation (Priority: P6)

As a family admin, I want each family member to have their own isolated portfolio so that financial data remains private unless explicitly shared.

**Why this priority**: Data isolation is a core architectural requirement but is primarily transparent to users after initial setup.

**Independent Test**: Can be fully tested by logging in as different users and verifying each sees only their own data.

**Acceptance Scenarios**:

1. **Given** two family members (Alice and Bob), **When** Alice views her portfolio, **Then** she sees only her own transactions and cannot see Bob's data.

2. **Given** I am Alice, **When** I search for transactions, **Then** the results only include my own transactions, regardless of search criteria.

---

### User Story 7 - Dashboard with Historical Returns and Market Context (Priority: P7)

As an investor, I want to see my portfolio's historical performance and current market valuation context so that I can make informed investment decisions.

**Why this priority**: Historical returns and market context provide strategic insights but require core functionality (transactions, quotes) to be in place first.

**Independent Test**: Can be fully tested by viewing the dashboard after recording transactions spanning multiple years and verifying historical returns are calculated correctly.

**Acceptance Scenarios**:

1. **Given** I have transactions from 2022, 2023, and 2024, **When** I view the dashboard, **Then** I see annual returns for each year based on year-end valuations.

2. **Given** the current date is mid-2025, **When** I view the dashboard, **Then** I see Year-to-Date (YTD) return calculated from Jan 1, 2025 valuation to current value.

3. **Given** I have multiple positions, **When** I view the dashboard, **Then** I see each position's allocation weight (%) and historical annual returns.

4. **Given** the Research Affiliates API is available, **When** I view the dashboard, **Then** I see the current Global CAPE value with valuation context (e.g., "Above historical median").

---

### Edge Cases

- What happens when a user enters a sell transaction for more shares than they own?
  - System MUST reject the transaction and display an error.

- What happens when exchange rate is entered as 0 or negative?
  - System MUST validate and reject invalid exchange rates.

- How does the system handle a stock split?
  - System MUST allow recording stock split events that adjust share count without affecting cost basis.

- What happens when Currency Ledger balance goes negative due to concurrent transactions?
  - System MUST use optimistic locking or similar mechanism to prevent race conditions.

- How does the system handle ticker symbols that change (e.g., corporate actions)?
  - System MUST support manual ticker updates with audit trail.

- What happens when stock quote API is unavailable?
  - System displays "無法取得報價" error message and uses cached data if available.

- What happens when a US ticker can't be found?
  - System falls back to UK market (.L suffix) before reporting error (useful for dual-listed ETFs like VWRA).

## Requirements *(mandatory)*

### Functional Requirements

#### Portfolio Management
- **FR-001**: System MUST record stock transactions with: Date, Ticker, Transaction Type (Buy/Sell), Shares (up to 4 decimal places), Price per Share (source currency), Transaction Fees, and Exchange Rate.
- **FR-001a**: System MUST allow users to edit or delete any previously recorded transaction.
- **FR-001b**: System MUST automatically recalculate all derived values (moving average cost, unrealized PnL, position totals) when any transaction is added, modified, or deleted.
- **FR-002**: System MUST calculate Total Cost (Source) = (Shares × Price) + Fees for each transaction.
- **FR-003**: System MUST calculate Total Cost (Home) = Total Cost (Source) × Exchange Rate for each transaction.
- **FR-004**: System MUST calculate and display moving average cost per share for each position in source currency.
- **FR-005**: System MUST calculate XIRR (Extended Internal Rate of Return) for portfolio performance when current prices are provided.
- **FR-006**: System MUST calculate Unrealized PnL = (Current Market Price × Total Shares × Current Exchange Rate) - Total Cost (Home Currency).
- **FR-020**: System MUST create exactly one portfolio per user account automatically on first login.
- **FR-021**: System MUST NOT allow portfolio naming - each user has one default portfolio with optional description only.

#### Real-time Stock Quotes
- **FR-022**: System MUST provide "獲取報價" button for each position to fetch current market price.
- **FR-023**: System MUST display visual feedback for quote fetch status: loading spinner, success ("已更新"), or error ("無法取得報價").
- **FR-024**: System MUST provide "獲取全部報價" button to batch-fetch quotes for all positions.
- **FR-025**: System MUST cache fetched quotes in browser localStorage with timestamp.
- **FR-026**: System MUST load cached quotes (max 1 hour old) on Dashboard page load for approximate metrics.
- **FR-027**: System MUST support Taiwan (TW), US, and UK stock markets for quote fetching.
- **FR-028**: System MUST auto-detect market based on ticker format:
  - Taiwan: Pure digits or digits with letters (e.g., 2330, 00878, 6547M)
  - UK: Ends with ".L" (e.g., VWRA.L)
  - US: Default for alphabetic tickers (e.g., VTI, AAPL)
- **FR-029**: System MUST fallback from US to UK market when quote not found (for dual-listed securities).

#### Dashboard Analytics
- **FR-030**: System MUST display Global CAPE (Cyclically Adjusted P/E) data from Research Affiliates API.
- **FR-031**: System MUST cache CAPE data with daily refresh (data updates monthly, cache for 24 hours).
- **FR-032**: System MUST calculate and display historical annual returns for the portfolio.
- **FR-033**: System MUST calculate historical annual returns per position based on year-end valuations.
- **FR-034**: System MUST display allocation weight (%) for each position relative to total portfolio value.
- **FR-035**: System MUST calculate Year-to-Date (YTD) return based on Jan 1 valuation vs current value.
- **FR-036**: Historical returns calculation formula: ((Year-End Value - Year-Start Value - Net Contributions) / Year-Start Value) × 100.
- **FR-037**: System MUST fetch historical closing prices via API to calculate year-end valuations.
- **FR-038**: System MUST cache historical prices permanently (historical data does not change).
- **FR-039**: System MUST use Dec 31 closing price (or last trading day) for year-end valuation.

#### Currency Ledger
- **FR-007**: System MUST track foreign currency balance with weighted average cost methodology.
- **FR-007a**: System MUST allow users to edit or delete any previously recorded currency transaction.
- **FR-007b**: System MUST automatically recalculate weighted average rate and balance when any currency transaction is added, modified, or deleted.
- **FR-008**: System MUST support Exchange_Buy transactions that increase foreign balance and update weighted average rate using formula: New Avg Rate = ((Old Balance × Old Avg Rate) + (New Amount × New Rate)) / (Old Balance + New Amount).
- **FR-009**: System MUST support Exchange_Sell transactions that decrease foreign balance and realize FX Profit/Loss based on difference between transaction rate and weighted average rate.
- **FR-010**: System MUST support Interest transactions that increase balance with configurable cost basis (0 or market rate).
- **FR-011**: System MUST support Spend transactions that decrease foreign balance WITHOUT changing the weighted average rate.

#### Integration
- **FR-012**: System MUST allow users to select "Currency Ledger" as fund source when recording stock purchases.
- **FR-013**: System MUST automatically create a Spend transaction in Currency Ledger when stock purchase uses Currency Ledger funds.
- **FR-014**: System MUST validate sufficient Currency Ledger balance before allowing stock purchase.
- **FR-014a**: System MUST execute stock purchase and Currency Ledger deduction as an **atomic transaction** - both operations succeed together or fail together with complete rollback.

#### Realized PnL
- **FR-015**: System MUST calculate Realized PnL (Home) when stocks are sold using **Moving Average Cost method**: Realized Amount (Home) - Cost Basis (Home), where Cost Basis = Shares Sold × Average Cost per Share (Home).
- **FR-016**: System MUST display Return Rate % = (Realized Profit / Cost Basis) × 100.

#### Multi-Tenancy
- **FR-017**: System MUST isolate all portfolio data by user.
- **FR-018**: System MUST include user context filtering in all data queries using EF Core global query filters.
- **FR-019**: System MUST validate user ownership before any data access or modification.

### Key Entities

- **User**: Represents a family member with authentication credentials and owns one portfolio and currency ledgers.
- **Portfolio**: Contains holdings and transactions for a specific user. Each user has exactly one portfolio with optional description.
- **Stock Transaction**: Records buy/sell activity with date, ticker, shares, price, fees, and exchange rate.
- **Position**: Aggregated view of holdings for a specific ticker within a portfolio (derived from transactions).
- **Currency Ledger**: Tracks foreign currency holdings with balance and weighted average rate.
- **Currency Transaction**: Records currency exchanges, interest, and spend events.

## UI/UX Specifications

### Page Structure

1. **Home Page** (`/`)
   - Auto-redirects to user's portfolio (creates default if none exists)
   - No portfolio selection UI (single portfolio per user)

2. **Portfolio Page** (`/portfolio/:id`)
   - Header: "投資組合" with optional description (editable via pencil icon)
   - Actions: "+ 新增交易" button, "匯入" button
   - Performance Metrics: Total cost, current value, unrealized PnL, return percentage
   - Positions Grid: Card-based layout showing each holding
   - "獲取全部報價" button to batch-fetch all position prices
   - Full Transaction History: All transactions with edit/delete actions

3. **Position Detail Page** (`/portfolio/:id/position/:ticker`)
   - Header: Ticker symbol with market tag (台股/美股/英股)
   - "獲取報價" button with visual status feedback
   - Position Metrics: Shares, average cost, total cost, current price (after fetch)
   - Value section (after quote): Current value, unrealized PnL with percentage
   - Transaction List: Filtered to this ticker only

4. **Dashboard Page** (`/dashboard`)
   - **Market Context Section**:
     - Global CAPE (Cyclically Adjusted P/E) from Research Affiliates API
     - Display current value, expected return, and valuation percentile
     - Provides macro context for investment decisions
   - **Portfolio Summary Section**:
     - Total market value, total cost, unrealized PnL (amount + percentage)
     - Year-to-date (YTD) return
     - Annual XIRR (annualized return rate)
   - **Historical Returns Table**:
     - Portfolio historical returns by year (e.g., 2024: +15.2%, 2023: +8.7%)
     - Calculated from actual transaction data and year-end valuations
   - **Position Performance Grid**:
     - All positions with current value, unrealized PnL, allocation weight (%)
     - Historical returns per position by year
     - Sortable by performance, allocation, or alphabetically
   - Auto-loads cached quotes (max 1 hour) on page load
   - "獲取全部報價" button to refresh all position prices

### Display Conventions

- **Currency Display**: All home currency values shown as TWD integers (no decimals)
- **Source Currency**: Display with 2 decimal places
- **Shares**: Display with up to 4 decimal places
- **Percentages**: Display with 2 decimal places and +/- sign
- **Positive PnL**: Green color (`number-positive` class)
- **Negative PnL**: Red color (`number-negative` class)
- **No "USD → TWD" display**: System only supports TWD home currency with USD/TWD transactions

### Quote Caching Strategy

- **Cache Key Pattern**: `quote_cache_${ticker}`
- **Cache Structure**: `{ quote: StockQuoteResponse, updatedAt: string, market: StockMarketType }`
- **Position Card Cache TTL**: 5 minutes (for individual refresh)
- **Dashboard Cache TTL**: 1 hour (for page load pre-population)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can record a complete stock transaction in under 1 minute.
- **SC-002**: XIRR calculations match Excel's XIRR function within 0.01% tolerance for identical inputs.
- **SC-003**: Weighted average cost calculations are accurate to 4 decimal places.
- **SC-004**: System supports up to 10 concurrent family members without data leakage.
- **SC-005**: Portfolio performance metrics (PnL, Average Cost) update within 5 seconds of quote fetch.
- **SC-006**: Users report 90% reduction in time spent on portfolio tracking compared to spreadsheet.
- **SC-007**: Zero incidents of cross-user data visibility after 3 months of usage.
- **SC-008**: System remains responsive (pages load under 3 seconds) with 10,000 transactions per user.
- **SC-009**: Quote fetch success rate above 95% for supported markets (TW, US, UK).
- **SC-010**: Cached quotes reduce API calls by 80% for repeat page visits.

## Clarifications

### Session 2026-01-06

- Q: 賣出股票時應使用哪種成本分配方法？ → A: 移動平均成本法 (Average Cost)
- Q: 股票購買與外幣帳本扣款失敗時的處理策略？ → A: 原子交易 (Atomic) - 使用資料庫交易 (ACID) 確保同時成功或同時失敗
- Q: 已記錄的交易是否可以編輯或刪除？ → A: 可自由編輯刪除 - 修改後系統自動重新計算所有衍生值 (加權平均成本、損益等)

### Session 2026-01-08

- Q: 一個帳號可以有多個投資組合嗎？ → A: 否，一個帳號只有一個投資組合，系統自動建立
- Q: 投資組合是否需要命名？ → A: 否，移除命名功能，只保留選填的描述欄位
- Q: 是否需要顯示 "USD → TWD" 標示？ → A: 否，系統目前只支援台幣與美金，最終報酬都以台幣呈現
- Q: 投資組合頁面應顯示哪些交易紀錄？ → A: 顯示全部持倉的交易紀錄（包含編輯/刪除功能）
- Q: 儀表板是否讀取快取的股價資料？ → A: 是，載入時自動讀取 1 小時內的 localStorage 快取
- Q: 儀表板應顯示哪些投資人關注的資訊？ → A:
  - 持倉的歷年報酬紀錄（各年度報酬率）
  - 投資組合的歷年報酬紀錄
  - 當前全球 CAPE（使用 Research Affiliates API）
  - 各持倉配置比重（%）

## Assumptions

- **Home Currency**: TWD (New Taiwan Dollar) is the only supported home currency. The system uses TWD for all final value calculations.
- **Foreign Currency**: USD is the primary investment currency. All stock prices are in source currency (typically USD).
- **Single Portfolio**: Each user account has exactly one portfolio, automatically created on first access.
- **Market Data**: Stock prices are fetched on-demand via external APIs (Sina Finance for TW, Yahoo Finance for US/UK). Quotes are cached in browser localStorage.
- **Authentication**: Standard email/password authentication via JWT tokens.
- **Interest Tracking**: Bank interest in Currency Ledger defaults to 0 cost basis (lowering average cost).
- **Stock Split Handling**: Stock splits are recorded as adjustment transactions that modify share count without affecting total cost basis.
- **Market Detection**: Ticker format determines market - digits for Taiwan, .L suffix for UK, otherwise US.

## Technology Stack

- **Backend**: C# .NET 8 with Entity Framework Core
- **Frontend**: TypeScript 5.x, React 18, Vite, TailwindCSS
- **Database**: SQLite (development), SQL Server compatible
- **Authentication**: JWT-based with refresh tokens
- **Stock Price API**: Sina Finance (TW), Yahoo Finance (US/UK) via server proxy
- **CAPE Data API**: Research Affiliates (https://interactive.researchaffiliates.com/asset-allocation-data/{YYYYMM}/{DD}/boxplot/boxplot_shillerpe.json)

## External API Reference

### Research Affiliates CAPE API

**Endpoint Pattern**: `https://interactive.researchaffiliates.com/asset-allocation-data/{YYYYMM}/{DD}/boxplot/boxplot_shillerpe.json`

**Date Discovery**: Data is updated monthly. Try current month days 01-10 first, then previous month.

**Response Format**: JSON array of objects with the following fields per region:
- `boxName`: Region identifier (e.g., "All Country", "USA", "Emerging Markets")
- `currentValue`: Current CAPE ratio
- `expectedValue`: Expected return based on CAPE
- `range50th`: Median historical CAPE
- `range25th`, `range75th`: Interquartile range

**Regions of Interest**:
- "All Country" - Global aggregate
- "USA" - US market
- "Emerging Markets" - EM aggregate

**Caching Strategy**: Cache for 24 hours (data updates monthly)

### Historical Price API

**Purpose**: Fetch historical closing prices for year-end valuation calculations.

**API Options by Market**:

1. **US/UK Stocks** - Yahoo Finance Historical Data
   - Endpoint: `https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={timestamp}&period2={timestamp}&interval=1d`
   - Returns OHLCV data for specified date range
   - Use `adjclose` for adjusted closing price

2. **Taiwan Stocks** - TWSE/TPEx Historical Data
   - TWSE: `https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={YYYYMMDD}&stockNo={ticker}`
   - TPEx: `https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_info/st43_result.php?d={YYY/MM/DD}&stkno={ticker}`

**Date Selection Logic**:
- For year-end: Use Dec 31, or last trading day before Dec 31 if market was closed
- For YTD start: Use Jan 1, or first trading day after Jan 1

**Caching Strategy**:
- Historical prices are immutable - cache permanently in database
- Store: ticker, date, close_price, exchange_rate (for that date)
- Only fetch once per ticker per date

**Database Table**: `historical_prices`
```
- Id: GUID
- Ticker: string
- Date: date
- ClosePrice: decimal (source currency)
- ExchangeRate: decimal (to home currency)
- CreatedAt: datetime
```


