# Feature Specification: TWD Ledger & Bank Accounts

**Feature Branch**: `005-twd-ledger-and-bank-accounts`
**Created**: 2026-01-28
**Status**: ✅ Complete
**Input**: Add TWD ledger (linked with TW stocks), bank accounts (interest rates/caps/estimation), total assets dashboard

## Background & Motivation

Currently, the system only has foreign currency ledger functionality for tracking exchange transactions, which can be linked with foreign stock transactions. Users want:
1. **TWD Ledger**: Track investment-purpose TWD funds, linked with TW stock transactions
2. **Bank Accounts**: Record total assets, preferential savings rates, and cap limits for each bank
3. **Total Assets Tracking**: Calculate Investment (stocks + ledgers) + Bank Assets = Total Assets
4. **Interest Estimation**: Calculate estimated monthly/yearly interest income based on rates and caps

## Architecture Design

### Portfolio-Ledger Binding Model (1:1)

```
┌─────────────────────────────────────────────────────────────────┐
│                    ONE USER'S INVESTMENT SYSTEM                  │
└─────────────────────────────────────────────────────────────────┘
                              │
    ┌─────────────────────────┼─────────────────────────┐
    ▼                         ▼                         ▼
┌─────────────┐       ┌─────────────┐           ┌─────────────┐
│ TWD Portfolio│       │ USD Portfolio│           │ Bank Accounts│
│ (台股)       │       │ (美股)       │           │ (存款)       │
└──────┬──────┘       └──────┬──────┘           └─────────────┘
       │ 1:1 Bind            │ 1:1 Bind
       ▼                     ▼
┌─────────────┐       ┌─────────────┐
│ TWD Ledger  │       │ USD Ledger  │
│ (投資資金)   │       │ (投資資金)   │
└─────────────┘       └─────────────┘
```

**Core Rules**:
- **1:1 Binding**: One Portfolio binds exactly one CurrencyLedger (mandatory, permanent)
- **One Currency Per User**: Each user can only have one ledger per currency (existing constraint)
- **Currency Match**: Stock transactions in a portfolio must match the bound ledger's currency
- **Auto-Linking**: All Buy/Sell transactions auto-create linked ledger transactions

### Total Assets View

```
┌─────────────────────────────────────────────────────────────────┐
│                        Total Assets                              │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┴───────────────────┐
          ▼                                       ▼
┌─────────────────────────┐           ┌─────────────────────────┐
│  Investment (⚡Auto)     │           │  Bank Accounts (Manual) │
├─────────────────────────┤           ├─────────────────────────┤
│ Stock Market Value      │           │ Bank A (Assets, Rate)   │
│ TWD Ledger Balance      │           │ Bank B (Assets, Rate)   │
│ Foreign Ledger Balance  │           │ Bank C (Assets, Rate)   │
└─────────────────────────┘           └─────────────────────────┘
      (Brokerage/Investment)              (General Bank Deposits)
```

### Calculation Logic

```
Investment = Stock Market Value + TWD Ledger Balance + Σ Foreign Ledger Balance (converted to TWD)
Bank Assets = Σ BankAccount.TotalAssets
Total Assets = Investment + Bank Assets

Monthly Interest = Σ Min(TotalAssets, InterestCap) × (InterestRate / 100 / 12)
Yearly Interest = Σ Min(TotalAssets, InterestCap) × (InterestRate / 100)
```

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create TWD Ledger and Record Deposits (Priority: P1)

User wants to create a TWD ledger to track investment-purpose TWD funds. After transferring money from bank to brokerage, record the deposit transaction in the TWD ledger.

**Why this priority**: TWD ledger is the foundation of this feature; subsequent stock linking and asset tracking depend on it.

**Independent Test**: Create TWD Ledger, add Deposit transaction, verify balance calculation.

**Acceptance Scenarios**:

1. **Given** user is logged in with no TWD ledger, **When** user creates a ledger with CurrencyCode=TWD, **Then** system creates successfully and displays in the list
2. **Given** user already has a TWD ledger, **When** user attempts to create another TWD ledger, **Then** system displays error "TWD ledger already exists"
3. **Given** user has TWD ledger with balance 0, **When** user adds Deposit transaction of 100,000 TWD, **Then** ledger balance shows 100,000 TWD

---

### User Story 2 - Stock Transaction Auto-Linked with Bound Ledger (Priority: P1)

When user buys/sells stocks, system automatically deducts/credits the corresponding amount from/to the bound ledger.

**Why this priority**: This is the core value of the 1:1 binding model, allowing users to track investment fund flows.

**Independent Test**: Create Portfolio with bound Ledger, create stock buy transaction, verify automatic Spend transaction creation.

**Acceptance Scenarios**:

1. **Given** Portfolio bound to USD Ledger with balance $10,000, **When** user buys AAPL for $5,000, **Then** USD Ledger auto-creates Spend transaction of $5,000, balance becomes $5,000
2. **Given** Portfolio bound to TWD Ledger with balance 100,000 TWD, **When** user buys 0050 for 50,000 TWD, **Then** TWD Ledger auto-creates Spend transaction of 50,000, balance becomes 50,000 TWD
3. **Given** Portfolio bound to Ledger with insufficient balance, **When** user attempts to buy stock exceeding balance, **Then** system displays insufficient balance error
4. **Given** user deletes a stock transaction, **When** that transaction has corresponding Spend/OtherIncome record, **Then** corresponding ledger transaction is also deleted, balance restored
5. **Given** Portfolio bound to USD Ledger, **When** user attempts to add TWD stock, **Then** system rejects with currency mismatch error

---

### User Story 3 - Create Bank Account with Preferential Rate Info (Priority: P2)

User wants to record total assets, preferential savings rates, and cap limits for each bank to track non-investment assets.

**Why this priority**: Bank accounts are necessary for total assets calculation, but can be developed after TWD ledger.

**Independent Test**: CRUD bank accounts, verify rate and cap fields are stored correctly.

**Acceptance Scenarios**:

1. **Given** user is logged in, **When** user creates bank account (Name: Taishin, TotalAssets: 500,000, Rate: 3.0%, Cap: 300,000), **Then** system creates successfully and displays in list
2. **Given** user has bank account, **When** user updates total assets or rate, **Then** system saves correctly and updates display
3. **Given** user has bank account, **When** user deletes account, **Then** account is marked inactive (soft delete)

---

### User Story 4 - View Interest Estimation (Priority: P2)

User wants to know estimated monthly/yearly interest income for each bank.

**Why this priority**: This is added value of bank account feature, helping users with financial planning.

**Independent Test**: Create multiple bank accounts, verify interest estimation calculation.

**Acceptance Scenarios**:

1. **Given** bank account (TotalAssets 500,000, Rate 3%, Cap 300,000), **When** user views interest estimation, **Then** shows monthly 750 TWD (300,000 × 3% / 12), yearly 9,000 TWD
2. **Given** bank account (TotalAssets 200,000, Rate 2.5%, Cap 500,000), **When** user views interest estimation, **Then** shows monthly 416.67 TWD (200,000 × 2.5% / 12, since TotalAssets < Cap)
3. **Given** multiple bank accounts, **When** user views interest estimation, **Then** shows each bank's interest details and total

---

### User Story 5 - View Total Assets Dashboard (Priority: P3)

User wants to see on one page: Investment (stocks + ledgers) + Bank Assets = Total Assets, and investment ratio.

**Why this priority**: This is an integration feature requiring all previous features to be complete.

**Independent Test**: With stocks, ledgers, and bank account data, verify total assets calculation and ratio display.

**Acceptance Scenarios**:

1. **Given** stock value 300,000, TWD ledger 50,000, USD ledger $3,000 (rate 32), bank assets 500,000, **When** user views total assets dashboard, **Then** shows: Investment 446,000 + Bank 500,000 = Total 946,000
2. **Given** above scenario, **When** user views investment ratio, **Then** shows Investment 47.1%, Bank 52.9%

---

### Edge Cases

- **Home Currency Exchange Rate**: When CurrencyCode == HomeCurrency (e.g., TWD), rate is fixed at 1.0, no exchange P&L calculation
- **Currency Mismatch**: When adding stock with different currency than bound ledger, system rejects the transaction
- **Empty Ledgers/Accounts**: When no data, total assets shows 0, should not error
- **Bank Rate is 0**: Allow setting 0 rate (like regular savings), interest estimation shows 0
- **Cap Greater Than TotalAssets**: Use smaller value (TotalAssets) when calculating interest
- **Portfolio Without Binding**: Not allowed - each portfolio must have a bound ledger

---

## Requirements *(mandatory)*

### Functional Requirements

**Portfolio-Ledger Binding (1:1 Model)**
- **FR-001**: Each Portfolio MUST be bound to exactly one CurrencyLedger (mandatory)
- **FR-002**: Each CurrencyLedger MUST be bound to exactly one Portfolio (1:1)
- **FR-002a**: CurrencyLedger MUST be created together with Portfolio (no standalone ledgers)
- **FR-002b**: When creating Portfolio, user MAY set an initial balance for the ledger
- **FR-003**: Binding is permanent and cannot be unbound
- **FR-004**: Each user can only have one Portfolio per currency (one TWD portfolio, one USD portfolio, etc.)
- **FR-005**: Stock transactions in a portfolio MUST have Currency == bound Ledger's CurrencyCode

**Home Currency Ledger (TWD)**
- **FR-006**: System MUST allow creating ledger with CurrencyCode=TWD (home currency)
- **FR-007**: When CurrencyCode == HomeCurrency, system MUST fix exchange rate at 1.0
- **FR-008**: When CurrencyCode == HomeCurrency, UI MUST hide exchange rate related fields

**Stock-Ledger Auto-Linking**
- **FR-009**: When buying stocks, system MUST auto-create linked Spend transaction in bound ledger
- **FR-010**: When selling stocks, system MUST auto-create linked OtherIncome transaction
- **FR-011**: When deleting/updating stock transaction, system MUST sync update/delete linked ledger transaction
- **FR-012**: When ledger balance is insufficient for Buy, system MUST prompt user to choose:
  - Option A: Auto-create Deposit transaction for the shortfall amount
  - Option B: Proceed without deposit (allow negative balance)
- **FR-012a**: System MUST NOT block transactions due to insufficient balance

**Bank Accounts**
- **FR-013**: System MUST provide BankAccount CRUD functionality
- **FR-014**: BankAccount MUST include: bank name, total assets, annual rate (%), interest cap
- **FR-015**: BankAccount and CurrencyLedger have no FK relationship
- **FR-016**: System MUST calculate interest estimation: Min(TotalAssets, Cap) × Rate / 12

**Total Assets**
- **FR-017**: System MUST calculate Investment = Stock Value + Σ Ledger Balance (converted to TWD)
- **FR-018**: System MUST calculate Total Assets = Investment + Σ Bank Account TotalAssets
- **FR-019**: System SHOULD display investment vs bank ratio pie chart

### Key Entities

- **CurrencyLedger (existing)**: Extend to support CurrencyCode=TWD
- **BankAccount (new)**: Bank name, total assets, rate, interest cap, note, is active

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: User can create TWD ledger and record first deposit within 1 minute
- **SC-002**: TW stock transactions auto-link to TWD ledger deduction, no manual recording needed
- **SC-003**: Interest estimation calculation accuracy 100% (verify against formula)
- **SC-004**: Total assets page load time < 2 seconds
- **SC-005**: No regression in existing foreign currency ledger and foreign stock functionality
