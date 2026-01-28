# Research: TWD Ledger & Bank Accounts

**Date**: 2026-01-28
**Feature**: 005-twd-ledger-and-bank-accounts

---

## §1 Existing Architecture Analysis

### CurrencyLedger Current State

| Concept | Current State |
|---------|---------------|
| **Entity** | `CurrencyLedger` - Foreign currency account, tracks holdings and weighted average cost |
| **Uniqueness** | Unique Index: (UserId, CurrencyCode) |
| **Currency Restriction** | No explicit restriction, technically can create TWD Ledger |
| **Transaction Types** | 9 types: ExchangeBuy/Sell, Interest, Spend, InitialBalance, Deposit/Withdraw, OtherIncome/Expense |
| **Portfolio Binding** | `Portfolio.BoundCurrencyLedgerId` - One-to-one binding |
| **Stock Linking** | Buying stocks auto-creates `Spend` type CurrencyTransaction |

### Cost Calculation Logic (CurrencyLedgerService)

```csharp
// Moving Weighted Average Method
// ExchangeBuy/Deposit/InitialBalance: Increase balance and cost
// ExchangeSell: Calculate realized P&L based on average cost
// Spend/Withdraw/OtherExpense: Deduct at average cost
// Interest/OtherIncome: Zero-cost balance increase
```

---

## §2 Design Decision Records

### Decision 1: TWD Ledger Globally Unique

**Problem**: Should TWD ledger be one per user, or one per Portfolio?

**Decision**: One per user (globally unique)

**Rationale**:
- Consistent with foreign currency ledgers (only one ledger per currency)
- Simplifies management, users don't need to decide which TWD ledger to use
- Existing Unique Index (UserId, CurrencyCode) already supports this constraint

---

### Decision 2: TWD Ledger Cost Basis

**Problem**: Does TWD ledger need cost basis calculation?

**Decision**: Adopt "Full Version (Unified Architecture)"

**Rationale**:
- Users may keep idle cash waiting for opportunities, this should be included in overall return calculation
- Unified architecture reduces maintenance cost
- UI hides meaningless fields based on CurrencyCode == HomeCurrency condition

**Implementation Details**:
```
// When CurrencyCode == HomeCurrency (TWD == TWD)
AverageExchangeRate = 1.0  // Fixed
RealizedPnl = 0            // Fixed
UnrealizedPnl = 0          // Fixed
TotalCost = Balance        // Balance equals cost
```

---

### Decision 3: Bank Account and Ledger Relationship

**Problem**: Should BankAccount have FK relationship with CurrencyLedger?

**Decision**: No relationship (separate operation)

**Rationale**:
- Bank accounts track "non-investment purpose" deposits
- Ledgers track "investment purpose" fund flows
- Different concepts, forced relationship adds complexity

---

### Decision 4: Total Assets Calculation Formula

**Problem**: How to define "Total Assets"?

**Decision**:
```
Investment = Stock Market Value + TWD Ledger Balance + Σ Foreign Ledger Balance (converted to TWD)
Bank Assets = Σ BankAccount.TotalAssets (manual input)
Total Assets = Investment + Bank Assets
```

**Rationale**:
- Investment money is at brokerage, bank deposits are non-investment funds
- Users need to see complete asset allocation (Investment vs Bank)
- Bank total assets manually input, avoids reconciliation requirements

---

### Decision 5: Interest Handling

**Problem**: Does interest need actual transaction records?

**Decision**: Only need estimation, no actual recording required

**Rationale**:
- Users mainly want to know "how much interest per month"
- Actual deposits can optionally be recorded in ledger (Interest type)
- Simplifies MVP scope

**Calculation Formula**:
```
Effective Principal = Min(TotalAssets, InterestCap)
Monthly Estimate = Effective Principal × (InterestRate / 100 / 12)
Yearly Estimate = Effective Principal × (InterestRate / 100)
```

---

### Decision 6: Development Order

**Phase 1**: TWD Ledger + TW Stock Linking
**Phase 2**: Bank Accounts + Interest Estimation
**Phase 3**: Total Assets Dashboard + Investment Ratio

**Rationale**:
- TWD ledger is core feature, must complete first
- Bank accounts depend on TWD ledger (for investment calculation)
- Total assets dashboard needs both previous features complete

---

## §3 Constraint Summary

### Hard Constraints

| ID | Constraint | Source |
|----|------------|--------|
| C1 | TWD ledger globally unique (one TWD Ledger per user) | User requirement |
| C2 | TWD ledger must link with TW stock transactions | User requirement |
| C3 | BankAccount and CurrencyLedger have no FK relationship | User requirement |
| C4 | Bank account total assets is manual input | User requirement |
| C5 | Interest needs no actual transaction records, only estimation | User requirement |
| C6 | Current phase: one bank one rate one cap (no tiered rates) | User requirement |

### Technical Constraints

| ID | Constraint | Source |
|----|------------|--------|
| T1 | Reuse existing CurrencyLedger Entity, only modify validation logic | Architecture consistency |
| T2 | TWD ledger rate fixed at 1.0, P&L fixed at 0 | Logical correctness |
| T3 | UI must hide fields based on CurrencyCode == HomeCurrency condition | UX simplicity |

---

## §4 Implementation Items

### Phase 1: TWD Ledger

**Backend**:
- [ ] Remove TWD ledger creation restriction (if any)
- [ ] CurrencyLedgerService TWD special handling (rate=1, P&L=0)
- [ ] CreateStockTransactionUseCase TW stock linking logic
- [ ] DeleteStockTransactionUseCase linked deletion logic

**Frontend**:
- [ ] Currency page supports creating TWD ledger
- [ ] CurrencyDetail page hides TWD irrelevant fields
- [ ] StockTransactionForm TW stocks can select TWD Ledger
- [ ] PortfolioSettings can bind TWD Ledger

### Phase 2: Bank Accounts

**Backend**:
- [ ] BankAccount Entity + Repository + CRUD Use Cases
- [ ] BankAccount Controller
- [ ] InterestEstimationService interest estimation calculation

**Frontend**:
- [ ] Bank account management page (CRUD)
- [ ] Interest estimation display (per-bank details + total)

### Phase 3: Total Assets Dashboard

**Backend**:
- [ ] TotalAssetsService total assets calculation
- [ ] New API: GET /api/assets/summary

**Frontend**:
- [ ] Total assets dashboard page
- [ ] Investment vs Bank ratio pie chart
