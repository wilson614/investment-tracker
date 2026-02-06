# Data Model: Bank Account Enhancements

**Feature**: 006-bank-account-enhancements
**Date**: 2026-02-06

## Entity Changes

### 1. BankAccount (MODIFY)

**Current Fields**:
```
- Id: Guid (PK)
- UserId: Guid (FK → User)
- BankName: string (max 100)
- TotalAssets: decimal
- InterestRate: decimal
- InterestCap: decimal
- Note: string? (max 500)
- IsActive: bool
- CreatedAt: DateTime
- UpdatedAt: DateTime
```

**New Fields**:
```
+ Currency: string (3 chars, default "TWD")
```

**Validation Rules**:
- Currency must be one of: TWD, USD, EUR, JPY, CNY, GBP, AUD
- Currency is required, cannot be null
- Default value for new accounts: "TWD"

**Migration Notes**:
- Add `Currency` column with default "TWD"
- All existing records get "TWD" as currency

---

### 2. FundAllocation (NEW)

**Fields**:
```
- Id: Guid (PK)
- UserId: Guid (FK → User)
- Purpose: AllocationPurpose (enum)
- Amount: decimal (in TWD, must be >= 0)
- CreatedAt: DateTime
- UpdatedAt: DateTime
```

**Relationships**:
- FundAllocation → User (Many-to-One)
- One user can have multiple allocations (one per purpose)

**Validation Rules**:
- Amount must be >= 0
- Sum of all user's allocations must not exceed total bank assets in TWD
- One allocation per purpose per user (unique constraint: UserId + Purpose)

**Indexes**:
- Unique: (UserId, Purpose)
- Foreign Key: UserId

---

### 3. AllocationPurpose (NEW ENUM)

**Values**:
```csharp
public enum AllocationPurpose
{
    EmergencyFund = 0,
    FamilyDeposit = 1,
    General = 2,
    Savings = 3
}
```

**Display Names (zh-TW)**:
- EmergencyFund → "緊急預備金"
- FamilyDeposit → "家庭存款"
- General → "一般"
- Savings → "儲蓄"

---

### 4. TotalAssetsSummary (MODIFY Response)

**Current Fields**:
```
- InvestmentTotal: decimal
- BankTotal: decimal
- GrandTotal: decimal
- InvestmentPercentage: decimal
- BankPercentage: decimal
- TotalMonthlyInterest: decimal
- TotalYearlyInterest: decimal
```

**New Fields**:
```
+ Allocations: List<AllocationSummary>
+ UnallocatedAmount: decimal
+ HasOverAllocation: bool
```

**AllocationSummary**:
```
- Purpose: string
- PurposeDisplay: string (localized name)
- Amount: decimal
- Percentage: decimal (of total bank assets)
```

---

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                          User                                │
│  - Id (PK)                                                   │
│  - Username                                                  │
│  - ...                                                       │
└──────────────────────┬───────────────────┬──────────────────┘
                       │                   │
                       │ 1:N               │ 1:N
                       ▼                   ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│       BankAccount           │   │      FundAllocation          │
│  - Id (PK)                  │   │  - Id (PK)                   │
│  - UserId (FK)              │   │  - UserId (FK)               │
│  - BankName                 │   │  - Purpose (enum)            │
│  - TotalAssets              │   │  - Amount                    │
│  - InterestRate             │   │  - CreatedAt                 │
│  - InterestCap              │   │  - UpdatedAt                 │
│  - Note                     │   │                              │
│  - IsActive                 │   │  UNIQUE(UserId, Purpose)     │
│  + Currency (NEW)           │   └─────────────────────────────┘
│  - CreatedAt                │
│  - UpdatedAt                │
└─────────────────────────────┘
```

---

## Database Migration Plan

### Migration 1: AddCurrencyToBankAccount

```sql
ALTER TABLE "BankAccounts"
ADD COLUMN "Currency" VARCHAR(3) NOT NULL DEFAULT 'TWD';
```

### Migration 2: CreateFundAllocationsTable

```sql
CREATE TABLE "FundAllocations" (
    "Id" UUID PRIMARY KEY,
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "Purpose" INTEGER NOT NULL,
    "Amount" DECIMAL(18,2) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "UQ_FundAllocation_User_Purpose" UNIQUE ("UserId", "Purpose")
);

CREATE INDEX "IX_FundAllocations_UserId" ON "FundAllocations" ("UserId");
```

---

## State Transitions

### FundAllocation Lifecycle

```
         ┌─────────┐
         │ (none)  │
         └────┬────┘
              │ Create
              ▼
         ┌─────────┐
         │ Active  │ ◄─────┐
         └────┬────┘       │
              │            │ Update
              ├────────────┘
              │
              │ Delete
              ▼
         ┌─────────┐
         │ Deleted │
         └─────────┘
```

**Notes**:
- No soft delete needed for FundAllocation (hard delete on removal)
- Update replaces amount; purpose cannot be changed (delete + create new)
