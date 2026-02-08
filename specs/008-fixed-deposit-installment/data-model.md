# Data Model: Fixed Deposit and Credit Card Installment Tracking

**Feature**: 008-fixed-deposit-installment
**Date**: 2026-02-08

## Entity Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                              User                                    │
│                         (existing entity)                            │
└─────────────────────────────────────────────────────────────────────┘
           │                        │                        │
           │ 1:N                    │ 1:N                    │ 1:N
           ▼                        ▼                        ▼
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│   BankAccount   │      │  FixedDeposit   │      │   CreditCard    │
│   (existing)    │      │     (NEW)       │      │     (NEW)       │
└─────────────────┘      └─────────────────┘      └─────────────────┘
                                                           │
                                                           │ 1:N
                                                           ▼
                                                  ┌─────────────────┐
                                                  │   Installment   │
                                                  │     (NEW)       │
                                                  └─────────────────┘
```

---

## New Entities

### 1. FixedDeposit

Represents a time deposit with locked principal.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK, Required | Owner of the deposit |
| BankName | string | Required, Max 100 | Bank name (text, not FK) |
| Principal | decimal | Required, >= 0 | Deposit amount |
| AnnualInterestRate | decimal | Required, >= 0 | Annual interest rate (e.g., 0.015 for 1.5%) |
| TermMonths | int | Required, >= 1 | Term length in months |
| StartDate | DateOnly | Required | When the deposit started |
| MaturityDate | DateOnly | Computed | StartDate + TermMonths |
| ExpectedInterest | decimal | Computed | Principal × Rate × (Term/12) |
| ActualInterest | decimal? | Optional | Recorded at closure (may differ if early withdrawal) |
| Currency | string | Required, 3 chars | Currency code (TWD, USD, etc.) |
| Status | FixedDepositStatus | Required | Active, Matured, Closed, EarlyWithdrawal |
| Note | string? | Max 500 | Optional notes |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Status Enum: FixedDepositStatus**
- `Active` (0): Deposit is locked, contributing to committed funds
- `Matured` (1): Term ended, awaiting user acknowledgment
- `Closed` (2): User acknowledged maturity, funds released
- `EarlyWithdrawal` (3): User withdrew before maturity

**Validation Rules**:
- StartDate cannot be in the future (max +1 day for timezone tolerance)
- MaturityDate = StartDate.AddMonths(TermMonths)
- Currency must be valid ISO code (TWD, USD, EUR, JPY, CNY, GBP, AUD)

---

### 2. CreditCard

Represents a credit card account that contains installment purchases.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK, Required | Owner of the card |
| BankName | string | Required, Max 100 | Issuing bank name |
| CardName | string | Required, Max 100 | Card nickname (e.g., "Costco聯名卡") |
| BillingCycleDay | int | 1-31 | Day of month for billing cycle |
| IsActive | bool | Default true | Soft delete flag |
| Note | string? | Max 500 | Optional notes |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Navigation Properties**:
- `Installments`: Collection of Installment entities

**Validation Rules**:
- BillingCycleDay must be between 1 and 31

---

### 3. Installment

Represents an installment purchase on a credit card.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| CreditCardId | Guid | FK, Required | Parent credit card |
| Description | string | Required, Max 200 | Purchase description |
| TotalAmount | decimal | Required, > 0 | Total payable amount (including any fees) |
| NumberOfInstallments | int | Required, >= 2 | Total number of installments |
| RemainingInstallments | int | Required, >= 0 | Remaining unpaid installments |
| MonthlyPayment | decimal | Computed | TotalAmount / NumberOfInstallments |
| StartDate | DateOnly | Required | First installment date |
| Status | InstallmentStatus | Required | Active, Completed, Cancelled |
| Note | string? | Max 500 | Optional notes |
| CreatedAt | DateTime | Auto | Record creation timestamp |
| UpdatedAt | DateTime | Auto | Last update timestamp |

**Status Enum: InstallmentStatus**
- `Active` (0): Ongoing installment, contributing to committed funds
- `Completed` (1): All payments made (RemainingInstallments = 0)
- `Cancelled` (2): Early payoff or cancelled

**Computed Properties** (not stored):
- `UnpaidBalance`: MonthlyPayment × RemainingInstallments
- `PaidAmount`: TotalAmount - UnpaidBalance
- `ProgressPercentage`: (NumberOfInstallments - RemainingInstallments) / NumberOfInstallments × 100

**Validation Rules**:
- NumberOfInstallments must be >= 2 (single payment is not an installment)
- RemainingInstallments cannot exceed NumberOfInstallments
- RemainingInstallments cannot be negative

---

## Database Schema

### Table: fixed_deposits

```sql
CREATE TABLE fixed_deposits (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    bank_name VARCHAR(100) NOT NULL,
    principal DECIMAL(18,2) NOT NULL CHECK (principal >= 0),
    annual_interest_rate DECIMAL(18,6) NOT NULL CHECK (annual_interest_rate >= 0),
    term_months INTEGER NOT NULL CHECK (term_months >= 1),
    start_date DATE NOT NULL,
    maturity_date DATE NOT NULL,
    expected_interest DECIMAL(18,2) NOT NULL,
    actual_interest DECIMAL(18,2),
    currency VARCHAR(3) NOT NULL DEFAULT 'TWD',
    status INTEGER NOT NULL DEFAULT 0,
    note VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_fixed_deposits_user_id ON fixed_deposits(user_id);
CREATE INDEX idx_fixed_deposits_status ON fixed_deposits(status);
CREATE INDEX idx_fixed_deposits_maturity_date ON fixed_deposits(maturity_date);
```

### Table: credit_cards

```sql
CREATE TABLE credit_cards (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    bank_name VARCHAR(100) NOT NULL,
    card_name VARCHAR(100) NOT NULL,
    billing_cycle_day INTEGER NOT NULL CHECK (billing_cycle_day BETWEEN 1 AND 31),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    note VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_credit_cards_user_id ON credit_cards(user_id);
CREATE INDEX idx_credit_cards_is_active ON credit_cards(is_active);
```

### Table: installments

```sql
CREATE TABLE installments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_card_id UUID NOT NULL REFERENCES credit_cards(id),
    description VARCHAR(200) NOT NULL,
    total_amount DECIMAL(18,2) NOT NULL CHECK (total_amount > 0),
    number_of_installments INTEGER NOT NULL CHECK (number_of_installments >= 2),
    remaining_installments INTEGER NOT NULL CHECK (remaining_installments >= 0),
    monthly_payment DECIMAL(18,2) NOT NULL,
    start_date DATE NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    note VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_remaining_not_exceed CHECK (remaining_installments <= number_of_installments)
);

CREATE INDEX idx_installments_credit_card_id ON installments(credit_card_id);
CREATE INDEX idx_installments_status ON installments(status);
```

---

## Migration Notes

**Migration Name**: `AddFixedDepositAndInstallment`

1. Create `fixed_deposits` table
2. Create `credit_cards` table
3. Create `installments` table with FK to credit_cards
4. Add indexes for query performance

**Rollback**: Drop tables in reverse order (installments → credit_cards → fixed_deposits)
