# Feature Specification: Fixed Deposit and Credit Card Installment Tracking

**Feature Branch**: `008-fixed-deposit-installment`
**Created**: 2026-02-08
**Status**: Draft
**Input**: User description: "Bank Account Fixed Deposit and Credit Card Installment Tracking - Support fixed deposits for bank accounts with maturity tracking, and credit card installment tracking to accurately calculate available funds"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Available vs Committed Funds (Priority: P1)

As a user, I want to see a clear breakdown of my total funds into "available funds" and "committed funds" so that I know exactly how much money I can freely use.

**Why this priority**: This is the core value proposition - users need accurate visibility into their true financial liquidity. Without this, users cannot make informed spending decisions.

**Independent Test**: Can be fully tested by viewing the dashboard summary showing total assets split into available and committed categories.

**Acceptance Scenarios**:

1. **Given** I have bank accounts with regular deposits and fixed deposits, **When** I view my asset summary, **Then** I see: Total Assets, Available Funds (liquid), and Committed Funds (fixed deposits + unpaid installments).
2. **Given** I have credit card installments with remaining payments, **When** I view my asset summary, **Then** the unpaid installment balance is shown as "Committed Funds" and deducted from available funds.
3. **Given** I have no fixed deposits or installments, **When** I view my asset summary, **Then** Available Funds equals Total Assets.

---

### User Story 2 - Create and Manage Fixed Deposits (Priority: P2)

As a user, I want to record my fixed deposits with their terms and maturity dates so that I can track when funds become available again.

**Why this priority**: Fixed deposits are a common savings instrument. Users need to track them to understand their non-liquid assets and plan for maturity dates.

**Independent Test**: Can be fully tested by creating a fixed deposit, viewing its details, and seeing it reflected in the committed funds calculation.

**Acceptance Scenarios**:

1. **Given** I want to record a new fixed deposit, **When** I create a fixed deposit with principal, interest rate, term length, and start date, **Then** the system calculates the maturity date and expected interest earnings.
2. **Given** I have an existing fixed deposit, **When** I view my fixed deposits list, **Then** I see all deposits with their principal, interest rate, maturity date, and days remaining.
3. **Given** a fixed deposit has matured, **When** I view my fixed deposits, **Then** the matured deposit is highlighted and I can mark it as "matured/closed".
4. **Given** I need to withdraw a fixed deposit early, **When** I record an early withdrawal, **Then** the system allows me to record the actual interest received (which may differ from expected due to penalty).

---

### User Story 3 - Track Credit Card Installment Purchases (Priority: P2)

As a user, I want to record installment purchases on my credit cards so that I can see how much of my money is committed to future payments.

**Why this priority**: Installment purchases are a hidden liability that reduces true available funds. Tracking them prevents overspending.

**Independent Test**: Can be fully tested by creating an installment purchase and seeing the unpaid balance reflected in committed funds.

**Acceptance Scenarios**:

1. **Given** I made an installment purchase, **When** I record the purchase with total amount, number of installments, and start date, **Then** the system calculates monthly payment amount and tracks remaining installments.
2. **Given** I have active installments, **When** I view my installments list, **Then** I see each installment's original amount, monthly payment, remaining installments, and total unpaid balance.
3. **Given** a monthly payment is due, **When** I record a payment, **Then** the remaining installments decrease by one and the unpaid balance is updated.
4. **Given** I want to pay off an installment early, **When** I record early payoff, **Then** the installment is marked as completed and removed from committed funds.

---

### User Story 4 - Manage Credit Cards (Priority: P3)

As a user, I want to manage my credit card accounts so that I can associate installment purchases with specific cards.

**Why this priority**: Credit cards are the container for installments. This is foundational but lower priority than the core tracking functionality.

**Independent Test**: Can be fully tested by creating a credit card account and viewing it in the list.

**Acceptance Scenarios**:

1. **Given** I want to add a credit card, **When** I create a card with bank name, card name, and billing cycle date, **Then** the card is added to my account.
2. **Given** I have credit cards, **When** I view my cards list, **Then** I see each card with its total active installments and total unpaid balance.
3. **Given** I no longer use a credit card, **When** I deactivate the card, **Then** it is hidden from active views but installment history is preserved.

---

### Edge Cases

- What happens when a fixed deposit matures? The system should notify/highlight but NOT automatically move funds. User must manually acknowledge.
- How does the system handle installments with 0% interest vs interest-bearing installments? System should support both; monthly payment = total amount / installments for 0% interest.
- What happens if user records a fixed deposit in a foreign currency? Fixed deposits should support currency selection, with committed funds converted to home currency (TWD) using exchange rates.
- What happens when installment has fees/interest on top of principal? User can enter total payable amount (including fees) as the installment amount.
- How does the system handle overdue installment payments? System does not track payment due dates; it only tracks remaining unpaid balance. All payments are treated as on-time.

## Requirements *(mandatory)*

### Functional Requirements

**Fixed Deposits:**
- **FR-001**: System MUST allow users to create fixed deposits with: principal amount, annual interest rate, term length (in months), start date, and optional note.
- **FR-002**: System MUST calculate maturity date based on start date and term length.
- **FR-003**: System MUST calculate expected interest earnings at maturity based on principal and rate.
- **FR-004**: System MUST display days remaining until maturity for each active deposit.
- **FR-005**: System MUST allow users to mark a fixed deposit as "matured" or "early withdrawal".
- **FR-006**: System MUST support fixed deposits in different currencies (TWD, USD, etc.).
- **FR-007**: System MUST associate fixed deposits with a bank (for organizational purposes).

**Credit Cards:**
- **FR-008**: System MUST allow users to create credit card accounts with: bank name, card name (nickname), and billing cycle date.
- **FR-009**: System MUST allow users to deactivate credit cards while preserving history.

**Installment Tracking:**
- **FR-010**: System MUST allow users to create installment purchases with: credit card, description, total amount, number of installments, and start date.
- **FR-011**: System MUST calculate monthly payment amount (total amount / number of installments).
- **FR-012**: System MUST track remaining installments and remaining unpaid balance.
- **FR-013**: System MUST allow users to record monthly payments (decrements remaining installments).
- **FR-014**: System MUST allow users to record early payoff (marks installment as completed).
- **FR-015**: System MUST automatically mark installments as completed when remaining installments reach zero.

**Available Funds Calculation:**
- **FR-016**: System MUST calculate "Available Funds" as: Total Bank Assets - Fixed Deposits Principal - Unpaid Installment Balance.
- **FR-017**: System MUST display a summary showing: Total Assets, Available Funds, and Committed Funds breakdown.
- **FR-018**: System MUST convert foreign currency amounts to TWD using current exchange rates for summary calculations.

### Key Entities

- **FixedDeposit**: Represents a time deposit with principal, rate, term, maturity date. Belongs to a User. Stores bank name as text field (not linked to BankAccount entity).
- **CreditCard**: Represents a credit card account with bank, name, billing date. Belongs to a User. Contains multiple Installments.
- **Installment**: Represents an installment purchase with total amount, number of installments, remaining installments, monthly payment. Belongs to a CreditCard.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create a fixed deposit in under 30 seconds by entering only required fields (principal, rate, term, start date).
- **SC-002**: Users can see their "true available funds" on the main dashboard within 2 seconds of page load.
- **SC-003**: 100% of fixed deposits display accurate days-to-maturity countdown.
- **SC-004**: 100% of active installments correctly reduce available funds by their unpaid balance.
- **SC-005**: Users can record an installment payment in under 15 seconds (single click for regular monthly payment).
- **SC-006**: Users can view all upcoming installment payments for the next 3 months in a single view.
- **SC-007**: System correctly handles multi-currency fixed deposits with accurate TWD conversion in summary.

## Clarifications

### Session 2026-02-08

- Q: Fixed Deposit 與 BankAccount 的關聯方式？ → A: 獨立實體，定存只需輸入銀行名稱（文字欄位），不關聯既有 BankAccount。
- Q: 分期付款逾期時系統如何處理？ → A: 忽略逾期概念，系統不追蹤付款時間，只計算剩餘未付總額，均視為如期還款。

## Assumptions

- Fixed deposits are tracked for informational purposes; the system does not integrate with actual bank systems.
- Installment payments are manually recorded by users; no automatic bank statement integration.
- Interest calculation for fixed deposits uses simple interest (principal × rate × term/12) unless user specifies otherwise.
- Credit card billing cycle date is informational; system does not enforce payment schedules.
- All monetary calculations round to 2 decimal places.
- Exchange rates are sourced from the existing exchange rate system in the application.
