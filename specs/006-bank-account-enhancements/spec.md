# Feature Specification: Bank Account Enhancements

**Feature Branch**: `006-bank-account-enhancements`
**Created**: 2026-02-06
**Status**: Draft
**Input**: User description: "Bank Account Enhancements - Multi-currency support, category classification, historical performance generalization, currency display consistency, and display logic fixes"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Foreign Currency Bank Account Support (Priority: P1)

As a user with foreign currency savings, I want to add bank accounts in different currencies (USD, EUR, JPY, etc.) so that I can track my complete financial picture including foreign currency holdings.

**Why this priority**: Users cannot currently track foreign currency bank accounts, which is a significant gap for users with international banking. This is foundational for accurate total assets calculation.

**Independent Test**: Can be fully tested by creating a bank account with a non-TWD currency and verifying it displays correctly with proper currency formatting.

**Acceptance Scenarios**:

1. **Given** I am on the bank account creation form, **When** I select a currency other than TWD (e.g., USD), **Then** the account is created with the selected currency and displays with proper currency formatting.
2. **Given** I have a USD bank account with balance $10,000, **When** I view the total assets dashboard, **Then** the USD amount is converted to TWD using current exchange rate and included in the total.
3. **Given** I have bank accounts in multiple currencies, **When** I view my bank accounts list, **Then** each account displays its balance in its native currency with proper formatting.

---

### User Story 2 - Fund Allocation for Bank Assets (Priority: P2)

As a user managing my finances, I want to allocate my total bank assets (across all accounts and currencies) into virtual categories like Emergency Fund and Family Deposit, so that I can see how my savings are mentally allocated for different purposes on the total assets dashboard.

**Why this priority**: Fund allocation enables better financial planning and mental accounting. Users can see at a glance how their total bank savings are designated for various purposes.

**Independent Test**: Can be fully tested by creating fund allocations against total bank assets and verifying the dashboard shows allocation breakdown with remaining unallocated amount.

**Acceptance Scenarios**:

1. **Given** I have total bank assets of NT$ 2,000,000, **When** I allocate NT$ 500,000 to "Emergency Fund", **Then** the allocation is saved and displayed on the total assets dashboard.
2. **Given** I have allocated NT$ 500,000 to Emergency Fund and NT$ 800,000 to Family Deposit, **When** I view the total assets dashboard, **Then** I see each allocation amount and the remaining unallocated amount (NT$ 700,000).
3. **Given** I try to allocate more than my total bank assets, **When** I submit the allocation, **Then** the system prevents over-allocation with an appropriate error message.

---

### User Story 3 - Historical Performance Multi-Currency Support (Priority: P3)

As a user with TWD-based or mixed-currency portfolios, I want the historical performance charts to correctly handle different base currencies so that I can see accurate performance data regardless of my portfolio's base currency.

**Why this priority**: Current implementation assumes USD as base currency. This fix ensures accurate reporting for all portfolio types.

**Independent Test**: Can be fully tested by creating a TWD-based portfolio with historical data and verifying the performance chart displays correct values without currency conversion errors.

**Acceptance Scenarios**:

1. **Given** I have a TWD-based portfolio (BaseCurrency=TWD), **When** I view the historical performance chart, **Then** the chart displays values in TWD without attempting USD conversion.
2. **Given** I have a USD-based portfolio, **When** I view the historical performance chart, **Then** the chart correctly converts values to the home currency (TWD) for display.
3. **Given** I switch between portfolios with different base currencies, **When** I view each portfolio's performance, **Then** each chart correctly handles its respective base currency.

---

### User Story 4 - Currency Display Consistency (Priority: P4)

As a user viewing my bank account information, I want consistent currency formatting across all bank account screens so that the display is professional and easy to read.

**Why this priority**: This is a UX polish item that improves the overall user experience but doesn't add new functionality.

**Independent Test**: Can be fully tested by navigating through all bank account-related screens and verifying currency values use consistent formatting.

**Acceptance Scenarios**:

1. **Given** I am viewing the bank accounts list page, **When** I compare currency formatting across all displayed values, **Then** all TWD values use the same format (e.g., "NT$ 1,234,567").
2. **Given** I am viewing a bank account card, **When** I look at balance, interest cap, and other monetary values, **Then** all use consistent formatting with proper thousand separators and currency symbols.
3. **Given** I have accounts in different currencies, **When** I view the accounts list, **Then** each currency uses its appropriate symbol and formatting conventions.

---

### User Story 5 - Interest Cap Zero Display Fix (Priority: P5)

As a user with a bank account that has an interest cap of 0 (no cap), I want the system to correctly display this as "0" rather than "無上限" so that the information is accurate.

**Why this priority**: This is a bug fix with clear requirements. Lower priority as it's a specific edge case.

**Independent Test**: Can be fully tested by creating an account with interestCap=0 and verifying it displays "0" not "無上限".

**Acceptance Scenarios**:

1. **Given** I have a bank account with interestCap set to 0, **When** I view the account details, **Then** the interest cap displays as "NT$ 0" (formatted currency value, meaning zero cap amount).
2. **Given** I have a bank account with interestCap set to null/undefined, **When** I view the account details, **Then** the interest cap displays as "無上限" (meaning no cap limit).
3. **Given** I have a bank account with interestCap set to 50000, **When** I view the account details, **Then** the interest cap displays as "NT$ 50,000".

---

### Edge Cases

- When exchange rate data is unavailable: System uses last known rate and displays visual indicator (e.g., warning icon or "stale" badge) to inform user
- How does the system handle a currency that is not in the supported list? Only supported currencies are selectable; unsupported currencies cannot be added
- What happens when total bank assets decrease below allocated amount? System shows warning that allocations exceed available assets; user must adjust allocations
- How are historical performance charts displayed when exchange rate history is incomplete? Use nearest available rate or interpolate

## Requirements *(mandatory)*

### Functional Requirements

**Foreign Currency Support**
- **FR-001**: System MUST allow users to select a currency when creating a bank account
- **FR-002**: System MUST support at minimum: TWD, USD, EUR, JPY, CNY, GBP, AUD
- **FR-003**: System MUST display bank account balances in their native currency with proper formatting
- **FR-004**: System MUST convert foreign currency balances to TWD for total assets calculation using current exchange rates
- **FR-016**: System MUST use last known exchange rate when current rate is unavailable, with visual indicator showing rate staleness

**Fund Allocation**
- **FR-005**: System MUST allow users to create fund allocations against total bank assets
- **FR-006**: System MUST support allocation purposes: Emergency Fund, Family Deposit, General, Savings
- **FR-007**: System MUST display fund allocation breakdown on the total assets dashboard (no clickable navigation)
- **FR-008**: System MUST calculate and display unallocated amount (total bank assets minus sum of allocations)
- **FR-017**: System MUST prevent over-allocation (sum of allocations cannot exceed total bank assets)

**Historical Performance**
- **FR-009**: System MUST handle portfolios with any base currency (not just USD)
- **FR-010**: System MUST use exchange rate of 1.0 when portfolio base currency matches home currency (TWD)
- **FR-011**: System MUST correctly convert portfolio values to home currency for display

**Display Consistency**
- **FR-012**: System MUST use consistent currency formatting across all bank account screens
- **FR-013**: System MUST display TWD with "NT$" prefix and thousand separators
- **FR-014**: System MUST display foreign currencies with their standard symbols

**Bug Fixes**
- **FR-015**: System MUST distinguish between interestCap=0, interestCap=null, and interestCap=value in display logic

### Key Entities

- **BankAccount**: Extended with `Currency` field (string, e.g., "TWD", "USD") for multi-currency support
- **FundAllocation** (new): Represents virtual allocation of bank assets to a purpose. Fields: Purpose (enum: EmergencyFund, FamilyDeposit, General, Savings), Amount (decimal in TWD)
- **TotalAssetsSummary**: Extended to include fund allocation breakdown with amounts and unallocated remainder
- **ExchangeRate**: Existing entity used for currency conversion

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create and view bank accounts in at least 7 different currencies with proper formatting
- **SC-002**: Users can create fund allocations and see accurate allocation totals plus unallocated remainder on the dashboard
- **SC-003**: Total assets dashboard correctly reflects the sum of all accounts converted to TWD
- **SC-004**: Historical performance charts display accurate data for both TWD-based and USD-based portfolios
- **SC-005**: All currency displays across bank account features use consistent formatting (100% consistency)
- **SC-006**: Interest cap of 0 displays correctly, distinguishable from null/undefined (bug eliminated)

## Clarifications

### Session 2026-02-06

- Q: How should existing bank account data be migrated for new Currency field? → A: Default existing accounts to Currency=TWD
- Q: How should the system handle unavailable exchange rate data? → A: Use last known rate with visual indicator showing rate is stale
- Q: What is the Category feature design? → A: Category is NOT per-account. It's a virtual fund allocation on the total bank assets. Users allocate portions of total bank balance (across all currencies, converted to TWD) to purposes like Emergency Fund, Family Deposit, etc. No clickable navigation needed.

## Assumptions

- Exchange rate data is available through the existing exchange rate service
- Existing bank accounts will be migrated with default value: Currency=TWD
- Supported currencies are commonly used international currencies (TWD, USD, EUR, JPY, CNY, GBP, AUD)
- Fund allocation purposes are fixed and not user-customizable in this release
- Users cannot create custom allocation purposes in this version
- Home currency for total assets calculation is always TWD
- Fund allocations are virtual/mental accounting only; they do not affect actual bank account data
