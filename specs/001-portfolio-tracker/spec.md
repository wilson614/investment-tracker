# Feature Specification: Family Investment Portfolio Tracker

**Feature Branch**: `001-portfolio-tracker`
**Created**: 2026-01-06
**Status**: Draft
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

### User Story 4 - View Portfolio Performance (Priority: P4)

As a family member, I want to see my portfolio performance metrics (XIRR, Unrealized PnL, Average Cost) so that I can evaluate my investment returns.

**Why this priority**: Performance metrics provide actionable insights but require the foundational transaction data from P1-P3 to be meaningful.

**Independent Test**: Can be fully tested by recording several transactions and verifying calculated metrics match expected values based on known formulas.

**Acceptance Scenarios**:

1. **Given** I have multiple buy transactions for VWRA over time, **When** I view the portfolio, **Then** I see the moving average cost per share in both source currency (USD) and home currency (TWD).

2. **Given** current market price for VWRA is $130 USD and exchange rate is 32.00, **When** I view unrealized PnL, **Then** the system calculates: Unrealized PnL (Home) = (Current Price × Total Shares × Current FX Rate) - Total Cost (Home Currency).

3. **Given** my transaction history for a position, **When** I request XIRR, **Then** the system calculates the annualized return rate based on cash flows (investment dates/amounts) and current market value.

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

## Requirements *(mandatory)*

### Functional Requirements

#### Portfolio Management
- **FR-001**: System MUST record stock transactions with: Date, Ticker, Transaction Type (Buy/Sell), Shares (up to 4 decimal places), Price per Share (source currency), Transaction Fees, and Exchange Rate.
- **FR-001a**: System MUST allow users to edit or delete any previously recorded transaction.
- **FR-001b**: System MUST automatically recalculate all derived values (moving average cost, unrealized PnL, position totals) when any transaction is added, modified, or deleted.
- **FR-002**: System MUST calculate Total Cost (Source) = (Shares × Price) + Fees for each transaction.
- **FR-003**: System MUST calculate Total Cost (Home) = Total Cost (Source) × Exchange Rate for each transaction.
- **FR-004**: System MUST calculate and display moving average cost per share for each position.
- **FR-005**: System MUST calculate XIRR (Extended Internal Rate of Return) for portfolio performance.
- **FR-006**: System MUST calculate Unrealized PnL = (Current Market Price × Total Shares) - Total Cost (Home Currency).

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
- **FR-018**: System MUST include user context filtering in all data queries.
- **FR-019**: System MUST validate user ownership before any data access or modification.

### Key Entities

- **User**: Represents a family member with authentication credentials and owns portfolios/currency ledgers.
- **Portfolio**: Contains holdings and transactions for a specific user. A user may have multiple portfolios.
- **Stock Transaction**: Records buy/sell activity with date, ticker, shares, price, fees, and exchange rate.
- **Position**: Aggregated view of holdings for a specific ticker within a portfolio (derived from transactions).
- **Currency Ledger**: Tracks foreign currency holdings with balance and weighted average rate.
- **Currency Transaction**: Records currency exchanges, interest, and spend events.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can record a complete stock transaction in under 1 minute.
- **SC-002**: XIRR calculations match Excel's XIRR function within 0.01% tolerance for identical inputs.
- **SC-003**: Weighted average cost calculations are accurate to 4 decimal places.
- **SC-004**: System supports up to 10 concurrent family members without data leakage.
- **SC-005**: Portfolio performance metrics (PnL, Average Cost) update within 5 seconds of transaction entry.
- **SC-006**: Users report 90% reduction in time spent on portfolio tracking compared to spreadsheet.
- **SC-007**: Zero incidents of cross-user data visibility after 3 months of usage.
- **SC-008**: System remains responsive (pages load under 3 seconds) with 10,000 transactions per user.

## Clarifications

### Session 2026-01-06

- Q: 賣出股票時應使用哪種成本分配方法？ → A: 移動平均成本法 (Average Cost)
- Q: 股票購買與外幣帳本扣款失敗時的處理策略？ → A: 原子交易 (Atomic) - 使用資料庫交易 (ACID) 確保同時成功或同時失敗
- Q: 已記錄的交易是否可以編輯或刪除？ → A: 可自由編輯刪除 - 修改後系統自動重新計算所有衍生值 (加權平均成本、損益等)

## Assumptions

- **Home Currency**: TWD (New Taiwan Dollar) is assumed as the home currency. The system could be extended to support configurable home currency in future iterations.
- **Foreign Currency**: Initial implementation focuses on USD as the primary foreign currency. Multi-currency support can be added later.
- **Market Data**: Current market prices will be entered manually by users. Automatic price feeds are out of scope for MVP.
- **Authentication**: Standard email/password authentication. OAuth integration is not required for family use.
- **Interest Tracking**: Bank interest in Currency Ledger defaults to 0 cost basis (lowering average cost).
- **Stock Split Handling**: Stock splits are recorded as adjustment transactions that modify share count without affecting total cost basis.
