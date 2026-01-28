# Feature Specification: TWD Ledger & Bank Accounts

**Feature Branch**: `005-twd-ledger-and-bank-accounts`
**Created**: 2026-01-28
**Status**: Draft
**Input**: Add TWD ledger (linked with TW stocks), bank accounts (interest rates/caps/estimation), total assets dashboard

## Background & Motivation

Currently, the system only has foreign currency ledger functionality for tracking exchange transactions, which can be linked with foreign stock transactions. Users want:
1. **TWD Ledger**: Track investment-purpose TWD funds, linked with TW stock transactions
2. **Bank Accounts**: Record total assets, preferential savings rates, and cap limits for each bank
3. **Total Assets Tracking**: Calculate Investment (stocks + ledgers) + Bank Assets = Total Assets
4. **Interest Estimation**: Calculate estimated monthly/yearly interest income based on rates and caps

## Architecture Design

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

### User Story 2 - TW Stock Transaction Linked with TWD Ledger (Priority: P1)

When user buys TW stocks, system automatically deducts the corresponding amount from TWD ledger, similar to foreign currency ledger linking.

**Why this priority**: This is the core value of TWD ledger, allowing users to track investment fund flows.

**Independent Test**: Bind Portfolio to TWD Ledger, create TW stock buy transaction, verify automatic Spend transaction creation.

**Acceptance Scenarios**:

1. **Given** Portfolio bound to TWD Ledger with balance 100,000 TWD, **When** user buys 0050 for 50,000 TWD, **Then** TWD Ledger auto-creates Spend transaction of 50,000, balance becomes 50,000 TWD
2. **Given** Portfolio bound to TWD Ledger with balance 30,000 TWD, **When** user attempts to buy stock for 50,000 TWD, **Then** system displays insufficient balance error
3. **Given** user deletes a TW stock transaction, **When** that transaction has corresponding Spend record, **Then** corresponding Spend transaction is also deleted, balance restored

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

- **TWD Ledger Exchange Rate**: When CurrencyCode=TWD and HomeCurrency=TWD, rate is fixed at 1.0, no exchange P&L calculation
- **Empty Ledgers/Accounts**: When no data, total assets shows 0, should not error
- **Bank Rate is 0**: Allow setting 0 rate (like regular savings), interest estimation shows 0
- **Cap Greater Than TotalAssets**: Use smaller value (TotalAssets) when calculating interest

---

## Requirements *(mandatory)*

### Functional Requirements

**TWD Ledger**
- **FR-001**: System MUST allow users to create ledger with CurrencyCode=TWD
- **FR-002**: System MUST ensure each user can only have one TWD ledger (globally unique)
- **FR-003**: TWD ledger MUST support all existing transaction types (Deposit, Withdraw, Interest, Spend, etc.)
- **FR-004**: When CurrencyCode == HomeCurrency, system MUST fix exchange rate at 1.0
- **FR-005**: When CurrencyCode == HomeCurrency, UI MUST hide exchange rate related fields (average rate, unrealized P&L)

**TW Stock Linking**
- **FR-006**: Portfolio MUST be able to bind TWD Ledger (existing BoundCurrencyLedgerId field)
- **FR-007**: When buying TW stocks, system MUST auto-create linked Spend transaction (like foreign currency ledger)
- **FR-008**: When selling TW stocks, system MUST auto-create linked OtherIncome transaction (return funds)
- **FR-009**: When deleting/updating stock transaction, system MUST sync update/delete linked ledger transaction

**Bank Accounts**
- **FR-010**: System MUST provide BankAccount CRUD functionality
- **FR-011**: BankAccount MUST include: bank name, total assets, annual rate (%), interest cap
- **FR-012**: BankAccount and CurrencyLedger have no FK relationship
- **FR-013**: System MUST calculate interest estimation: Min(TotalAssets, Cap) × Rate / 12

**Total Assets**
- **FR-014**: System MUST calculate Investment = Stock Value + Σ Ledger Balance (converted to TWD)
- **FR-015**: System MUST calculate Total Assets = Investment + Σ Bank Account TotalAssets
- **FR-016**: System SHOULD display investment vs bank ratio pie chart

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
