# Data Model: Fixed Deposit and Credit Card Installment Tracking

**Feature**: 008-fixed-deposit-installment
**Date**: 2026-02-08

## Entity Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                              User                                   │
│                        (existing entity)                            │
└─────────────────────────────────────────────────────────────────────┘
           │                         │
           │ 1:N                     │ 1:N
           ▼                         ▼
┌─────────────────┐         ┌─────────────────┐
│   BankAccount   │         │   CreditCard    │
│ (extended model)│         │     (NEW)       │
└─────────────────┘         └─────────────────┘
                                      │
                                      │ 1:N
                                      ▼
                             ┌─────────────────┐
                             │   Installment   │
                             │     (NEW)       │
                             └─────────────────┘
```

---

## Entities

### 1. BankAccount (Extended for Fixed Deposits)

Bank accounts now represent both regular savings accounts and fixed-deposit accounts.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK, Required | Owner of the account |
| BankName | string | Required, Max 100 | Bank name |
| TotalAssets | decimal | Required, >= 0 | Account balance; for fixed deposit, this is principal |
| InterestRate | decimal | Required, >= 0 | Annual interest rate (%) |
| InterestCap | decimal | Required, >= 0 | Preferential cap (used by savings accounts) |
| Currency | string | Required, 3 chars | Currency code (TWD, USD, etc.) |
| Note | string? | Max 500 | Optional notes |
| IsActive | bool | Default true | Soft-delete flag |
| AccountType | BankAccountType | Required | Savings or FixedDeposit |
| TermMonths | int? | Optional, > 0 when fixed deposit | Term length in months |
| StartDate | DateTime? | Optional | Fixed deposit start date |
| MaturityDate | DateTime? | Computed for fixed deposit | StartDate + TermMonths |
| ExpectedInterest | decimal? | Computed for fixed deposit | Principal × annualRate × (term/12) |
| ActualInterest | decimal? | Optional | Actual interest at closure/withdrawal |
| FixedDepositStatus | FixedDepositStatus? | Optional | Active, Matured, Closed, EarlyWithdrawal |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Enums**:

`BankAccountType`
- `Savings` (0)
- `FixedDeposit` (1)

`FixedDepositStatus`
- `Active` (0)
- `Matured` (1)
- `Closed` (2)
- `EarlyWithdrawal` (3)

**Validation/Behavior Notes**:
- Non-fixed-deposit accounts clear fixed-deposit-only fields.
- `StartDate` cannot be too far in the future (max +1 day tolerance).
- Fixed-deposit computed fields are recalculated when principal/rate/term/start changes.

---

### 2. CreditCard

Represents a credit card account that contains installment purchases.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK, Required | Owner of the card |
| BankName | string | Required, Max 100 | Issuing bank name |
| CardName | string | Required, Max 100 | Card nickname |
| PaymentDueDay | int | Required, 1-31 | Monthly payment due day |
| Note | string? | Max 500 | Optional notes |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Navigation Properties**:
- `Installments`: Collection of Installment entities

**Validation Rules**:
- `PaymentDueDay` must be between 1 and 31.

---

### 3. Installment

Represents an installment purchase on a credit card.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| CreditCardId | Guid | FK, Required | Parent credit card |
| UserId | Guid | Required | Installment owner |
| Description | string | Required, Max 200 | Purchase description |
| TotalAmount | decimal | Required, > 0 | Total payable amount |
| NumberOfInstallments | int | Required, >= 1 | Total installment count |
| RemainingInstallments | int | Stored | Persisted value; API uses dynamic remaining count |
| MonthlyPayment | decimal | Computed | TotalAmount / NumberOfInstallments |
| FirstPaymentDate | DateTime | Required | First scheduled payment date |
| Status | InstallmentStatus | Required | Active, Completed, Cancelled |
| Note | string? | Max 500 | Optional notes |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Status Enum: InstallmentStatus**
- `Active` (0)
- `Completed` (1)
- `Cancelled` (2)

**Computed Runtime Properties** (not persisted separately):
- `PaidInstallments`: calculated from current date, `FirstPaymentDate`, and credit card `PaymentDueDay`
- `RemainingInstallments`: effective remaining count from `GetRemainingInstallments(paymentDueDay)`
- `UnpaidBalance`: `MonthlyPayment × effectiveRemainingInstallments`
- `PaidAmount`: `TotalAmount - UnpaidBalance`
- `ProgressPercentage`: `(NumberOfInstallments - effectiveRemainingInstallments) / NumberOfInstallments × 100`

**Validation Rules**:
- `TotalAmount` must be > 0.
- `NumberOfInstallments` must be > 0.
- `RemainingInstallments` cannot be negative or exceed `NumberOfInstallments`.
- `FirstPaymentDate` cannot be more than 1 year in the past.

---

## Database Schema

### Table: bank_accounts

```sql
CREATE TABLE bank_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    bank_name VARCHAR(100) NOT NULL,
    total_assets DECIMAL(18,2) NOT NULL CHECK (total_assets >= 0),
    interest_rate DECIMAL(18,4) NOT NULL CHECK (interest_rate >= 0),
    interest_cap DECIMAL(18,2) NOT NULL CHECK (interest_cap >= 0),
    currency VARCHAR(3) NOT NULL DEFAULT 'TWD',
    note VARCHAR(500),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    account_type INTEGER NOT NULL DEFAULT 0,
    term_months INTEGER NULL,
    start_date TIMESTAMP WITH TIME ZONE NULL,
    maturity_date TIMESTAMP WITH TIME ZONE NULL,
    expected_interest DECIMAL(18,2) NULL,
    actual_interest DECIMAL(18,2) NULL,
    fixed_deposit_status INTEGER NULL,

    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bank_accounts_user_id ON bank_accounts(user_id);
CREATE INDEX idx_bank_accounts_account_type ON bank_accounts(account_type);
```

### Table: credit_cards

```sql
CREATE TABLE credit_cards (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    bank_name VARCHAR(100) NOT NULL,
    card_name VARCHAR(100) NOT NULL,
    payment_due_day INTEGER NOT NULL CHECK (payment_due_day BETWEEN 1 AND 31),
    note VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_credit_cards_user_id ON credit_cards(user_id);
```

### Table: installments

```sql
CREATE TABLE installments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_card_id UUID NOT NULL REFERENCES credit_cards(id),
    user_id UUID NOT NULL,
    description VARCHAR(200) NOT NULL,
    total_amount DECIMAL(18,2) NOT NULL CHECK (total_amount > 0),
    number_of_installments INTEGER NOT NULL CHECK (number_of_installments > 0),
    remaining_installments INTEGER NOT NULL CHECK (remaining_installments >= 0),
    monthly_payment DECIMAL(18,2) NOT NULL,
    first_payment_date TIMESTAMP WITH TIME ZONE NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    note VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_installments_credit_card_id ON installments(credit_card_id);
CREATE INDEX idx_installments_user_id ON installments(user_id);
```

### Removed Table: fixed_deposits

`fixed_deposits` existed in the initial migration design but was removed after the merge strategy. Fixed-deposit data is now persisted in `bank_accounts` (`account_type = FixedDeposit` + fixed-deposit-specific columns).

---

## Migration Notes

**Relevant Migrations**:

1. `AddFixedDepositAndInstallment`
   - Created `fixed_deposits`, `credit_cards`, and `installments`.
2. `MergeFixedDepositIntoBankAccount`
   - Added fixed-deposit columns to `bank_accounts`.
   - Migrated data from `fixed_deposits` into `bank_accounts`.
   - Dropped `fixed_deposits`.
3. `RemoveCreditCardIsActive`
   - Removed soft-deactivation flag from `credit_cards`.
4. `RenameCreditCardBillingCycleDayToPaymentDueDay`
   - Renamed credit card day field for clarity.
5. `RenameStartDateToFirstPaymentDate`
   - Renamed installment date field.

**Rollback Considerations**:
- Reintroducing standalone `fixed_deposits` requires reverse data migration from `bank_accounts` where `account_type = FixedDeposit`.
- `payment_due_day` and `first_payment_date` would need reverse column renames if rolling back field naming changes.
