# Feature Specification: Ledger Exchange Cost Integration & Navigation Improvement

**Feature Branch**: `009-ledger-cost-nav`
**Created**: 2026-02-11
**Status**: Draft
**Input**: User description: "Use ledger LIFO exchange cost rate for stock transactions, fix AutoDeposit to use ExchangeBuy, fix navigation bar ledger button to remember last selection"

## Clarifications

### Session 2026-02-11

- Q: Should the LIFO-calculated exchange rate be shown to the user in the transaction form? → A: Hide the exchange rate input field; display the calculated rate as read-only information. If rate unavailable (no ledger records + API failure), show error directing user to create exchange records in the ledger first.
- Q: When balance is insufficient and user chooses margin (融資) with partial ledger balance, how is the exchange rate calculated? → A: Weighted blend — the portion covered by existing ledger balance uses the LIFO rate, the margin (uncovered) portion uses the transaction-date market rate.
- Q: Should the balance top-up always create an ExchangeBuy? → A: No, let the user choose the currency transaction type from available income types.
- Q: Should the user be able to manually input the exchange rate on the stock transaction form? → A: No, the exchange rate field is hidden. The rate is always system-calculated (LIFO or market rate fallback).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stock Purchase Uses Ledger Exchange Cost (Priority: P1)

When a user buys foreign-currency stocks, the system automatically calculates the exchange rate based on the user's actual currency acquisition history (LIFO weighted average from the currency ledger). The exchange rate field is not manually editable — the calculated rate is displayed as read-only information on the transaction form, providing transparency into the cost basis.

**Why this priority**: This is the core feature — accurate exchange cost tracking directly affects profit/loss calculations and investment performance reporting. Without this, the system reports incorrect cost bases for all foreign-currency stock transactions.

**Independent Test**: Can be tested by creating exchange buy records in a currency ledger, then buying a stock in that currency and verifying the stock transaction's exchange rate matches the LIFO-weighted average from the ledger, displayed as read-only on the form.

**Acceptance Scenarios**:

1. **Given** a USD ledger with two ExchangeBuy records (100 USD @ 30.5 TWD, then 200 USD @ 31.0 TWD), **When** the user buys a stock costing 150 USD, **Then** the system assigns an exchange rate calculated via LIFO: the newest 150 USD from the 200-USD layer at 31.0, resulting in rate = 31.0, displayed as read-only.
2. **Given** a USD ledger with ExchangeBuy and Interest income, **When** the user buys a stock, **Then** Interest income reduces the purchase amount needing an exchange rate but does not contribute to the exchange cost calculation (free money reduces cost basis).
3. **Given** a TWD stock purchase, **When** the transaction is created, **Then** the exchange rate is always 1.0 regardless of ledger history.
4. **Given** a foreign currency stock purchase where the ledger has no ExchangeBuy or InitialBalance records (only free income like Interest), **When** the system calculates the exchange rate, **Then** the system falls back to the historical market exchange rate for the transaction date.
5. **Given** a foreign currency stock purchase where the ledger has no records AND the market rate API fails, **When** the user attempts to create the transaction, **Then** the system shows an error directing the user to first create exchange records in the currency ledger.
6. **Given** a stock transaction form for a foreign currency stock, **When** the form is displayed, **Then** the exchange rate field is read-only (not manually editable) and shows the system-calculated rate.

---

### User Story 2 - Insufficient Balance Handling with Three Options (Priority: P1)

When a stock purchase would cause the currency ledger balance to go negative, the system presents the user with three options: (1) Margin — allow negative balance and proceed, (2) Top Up Balance — create a currency transaction to cover the shortfall, or (3) Cancel — abort the transaction. This replaces the previous AutoDeposit behavior.

**Why this priority**: Proper handling of insufficient balance is tightly coupled with exchange cost tracking (Story 1). The margin option enables real-world scenarios where users buy first and fund later, while the top-up option ensures accurate exchange cost records.

**Independent Test**: Can be tested by attempting a stock purchase with insufficient ledger balance and verifying each of the three options produces the correct outcome.

**Acceptance Scenarios**:

1. **Given** a USD ledger with 100 USD balance (from ExchangeBuy @ 30.5) and a stock purchase costing 200 USD, **When** the user chooses "Margin", **Then** the system creates the stock transaction, the ledger balance goes to -100 USD, and the exchange rate is a weighted blend: 100 USD at LIFO rate (30.5) + 100 USD at transaction-date market rate.
2. **Given** a USD ledger with 0 USD balance and a stock purchase costing 200 USD, **When** the user chooses "Margin", **Then** the system creates the stock transaction with the transaction-date market rate (no LIFO layers to blend), and the ledger balance goes to -200 USD.
3. **Given** a USD ledger with insufficient balance, **When** the user chooses "Top Up Balance", **Then** the system presents a form where the user can select the currency transaction type (e.g., ExchangeBuy, Deposit, or other income types) and the shortfall amount is pre-filled.
4. **Given** the user selects "Top Up Balance" and chooses ExchangeBuy as the transaction type, **When** the top-up is confirmed, **Then** the system creates the currency transaction with the market exchange rate for the transaction date, then proceeds with the stock purchase using the updated LIFO calculation.
5. **Given** a USD ledger with insufficient balance, **When** the user chooses "Cancel", **Then** no stock transaction or currency transaction is created, and the user returns to the transaction form.
6. **Given** a TWD ledger with insufficient balance, **When** the user chooses "Top Up Balance", **Then** the system creates the selected transaction type with exchange rate = 1.0 and homeAmount = shortfall amount.
7. **Given** the user chooses "Top Up Balance" but the market exchange rate cannot be fetched, **When** the top-up type requires an exchange rate (e.g., ExchangeBuy), **Then** the system warns the user and allows them to manually enter the exchange rate for the top-up transaction only.

---

### User Story 3 - Ledger Navigation Remembers Last Selection (Priority: P2)

When the user clicks the "帳本" (Ledger) button in the navigation bar, the system should navigate to the last ledger the user was viewing, similar to how the portfolio navigation remembers the last selected portfolio.

**Why this priority**: This is a UX convenience improvement. The core data accuracy issues in Stories 1-2 are more critical, but this addresses a reported usability problem where the navigation always resets to a default ledger.

**Independent Test**: Can be tested by selecting a specific ledger, navigating away, then clicking the ledger nav button and verifying it returns to the previously selected ledger.

**Acceptance Scenarios**:

1. **Given** the user previously viewed the USD ledger, **When** they navigate away and click the "帳本" nav button, **Then** the system navigates directly to the USD ledger detail page.
2. **Given** the user has never selected a ledger before (fresh session), **When** they click the "帳本" nav button, **Then** the system navigates to the first available ledger (default behavior).
3. **Given** the user's previously selected ledger has been deleted, **When** they click the "帳本" nav button, **Then** the system navigates to the first available ledger and updates the stored selection.
4. **Given** the user has no ledgers, **When** they click the "帳本" nav button, **Then** the system shows an appropriate message directing them to create a portfolio first.

---

### Edge Cases

- What happens when the currency ledger has zero balance and the user chooses "Top Up Balance"? The top-up covers the entire purchase amount; the LIFO calculation then includes only the newly created transaction.
- What happens when multiple stock purchases occur on the same date? Each purchase should independently calculate its LIFO exchange rate based on the ledger state at that point.
- What happens when a stock purchase amount exceeds all available exchange-cost layers in the ledger (only free income remains)? The system uses market exchange rate as a fallback for the uncovered portion.
- What happens when the user edits or deletes a currency transaction that was used in a LIFO calculation? The exchange cost on existing stock transactions is not retroactively recalculated (historical records remain as-is).
- What happens when a user with margin (negative balance) later adds funds to the ledger? The negative balance is reduced, but past stock transactions' exchange rates are not recalculated.
- What happens when the user chooses "Top Up Balance" with a non-ExchangeBuy type (e.g., Deposit)? The transaction is created without exchange cost, which means it will not contribute to the LIFO exchange rate calculation but will restore the balance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When creating a Buy stock transaction for a foreign currency, the system MUST automatically calculate the exchange rate using the LIFO weighted-average method from the bound currency ledger's transaction history.
- **FR-002**: The LIFO calculation MUST only use ExchangeBuy and InitialBalance transactions as exchange-cost-bearing layers; Interest, OtherIncome, and Deposit transactions reduce the purchase allocation but do not contribute to the exchange rate.
- **FR-003**: The exchange rate field on the stock transaction form MUST be read-only (not manually editable). The system-calculated rate MUST be displayed to the user for transparency.
- **FR-004**: For TWD-denominated stock transactions, the system MUST use exchange rate = 1.0 regardless of ledger history.
- **FR-005**: When the ledger-based LIFO calculation cannot determine an exchange rate (no cost-bearing layers available), the system MUST fall back to the historical market exchange rate for the transaction date.
- **FR-006**: When the exchange rate cannot be determined by any method (no LIFO layers AND market rate API failure), the system MUST block the transaction and display an error directing the user to create exchange records in the ledger.
- **FR-007**: When a stock purchase would cause the ledger balance to go negative, the system MUST present three options: Margin (allow negative balance), Top Up Balance (create a currency transaction for the shortfall), or Cancel.
- **FR-008**: When the user chooses "Margin" with partial ledger balance, the stock transaction's exchange rate MUST be a weighted blend of the LIFO rate (for the covered portion) and the market rate (for the margin portion).
- **FR-009**: When the user chooses "Margin" with zero ledger balance, the stock transaction's exchange rate MUST use the transaction-date market rate entirely.
- **FR-010**: When the user chooses "Top Up Balance", the system MUST allow the user to select the currency transaction type from available income types (e.g., ExchangeBuy, Deposit, etc.) with the shortfall amount pre-filled.
- **FR-011**: Top-up currency transactions MUST be linked to the triggering stock transaction via a reference identifier.
- **FR-012**: The navigation bar ledger button MUST navigate to the user's last-selected ledger, persisted across sessions.
- **FR-013**: If no previously selected ledger exists or the stored selection is invalid, the navigation MUST fall back to the default ledger selection behavior.

### Key Entities

- **CurrencyTransaction**: Represents a currency ledger entry. Key attributes: transaction type (ExchangeBuy, Deposit, etc.), amount, exchange rate, home amount, related stock transaction reference.
- **StockTransaction**: Represents a stock buy/sell. Key attributes: exchange rate (system-calculated, read-only), transaction date.
- **CurrencyLedger**: The foreign currency account bound to a portfolio. Tracks balance and transaction history used for LIFO exchange cost calculation. Balance may go negative when margin is used.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All foreign-currency stock Buy transactions use the ledger-based LIFO weighted-average exchange rate (or weighted blend with market rate for margin scenarios) without manual exchange rate input.
- **SC-002**: When balance is insufficient, users are presented with three clear options (Margin / Top Up / Cancel) instead of the previous automatic deposit behavior.
- **SC-003**: Top-up transactions created through the balance shortfall flow carry proper exchange rate and home amount information based on the user's chosen transaction type.
- **SC-004**: Users clicking the ledger navigation button are taken to their last-viewed ledger without an intermediate redirect flash.
- **SC-005**: Existing stock transactions and currency transactions are not retroactively modified — changes apply only to new transactions going forward.

## Assumptions

- The existing `CalculateExchangeRateForPurchase` LIFO logic is correct and does not need modification — only its integration point needs to change.
- The market exchange rate used for fallback and margin blending comes from the same external exchange rate service already used in the system.
- The ledger navigation improvement is a frontend-only change; the backend API for ledger data does not need modification.
- Changes apply only to new transactions; existing transactions are not retroactively updated.
- Margin tracking (negative balance management, funding reminders) is out of scope for this feature and will be addressed in a future feature.
