# Research: Fixed Deposit and Credit Card Installment Tracking

**Feature**: 008-fixed-deposit-installment
**Date**: 2026-02-08

## Research Summary

This document captures research findings and decisions made during the planning phase.

---

## 1. Fixed Deposit Interest Calculation

### Decision
Use **simple interest** formula: `Interest = Principal × Rate × (Term / 12)`

### Rationale
- Taiwan fixed deposits typically use simple interest for terms under 1 year
- Aligns with user expectation for quick mental calculation verification
- Compound interest can be added later if needed

### Alternatives Considered
| Alternative | Rejected Because |
|-------------|------------------|
| Compound interest (monthly) | Adds complexity; most Taiwan bank fixed deposits < 1 year use simple interest |
| Bank-specific formulas | Would require external API integration; violates self-hosted principle |

---

## 2. Fixed Deposit Status Lifecycle

### Decision
Three-state lifecycle: `Active` → `Matured` → `Closed` (or `Active` → `EarlyWithdrawal`)

### Rationale
- **Active**: Principal is locked, counted as committed funds
- **Matured**: Term ended, awaiting user action (still counted as committed until acknowledged)
- **Closed**: User acknowledged maturity or early withdrawal; removed from committed funds
- **EarlyWithdrawal**: Special close state tracking penalty situation

### State Diagram
```
┌─────────┐     maturity date      ┌─────────┐     user action     ┌────────┐
│  Active │ ──────────────────────▶│ Matured │ ───────────────────▶│ Closed │
└─────────┘                        └─────────┘                      └────────┘
     │                                                                   ▲
     │              early withdrawal (user action)                       │
     └───────────────────────────────────────────────────────────────────┘
```

---

## 3. Installment Unpaid Balance Calculation

### Decision
`Unpaid Balance = Monthly Payment × Remaining Installments`

### Rationale
- Simple and predictable calculation
- Monthly Payment is pre-calculated at creation: `Total Amount / Number of Installments`
- No need to track individual payment dates (per clarification: ignore overdue concept)

### Edge Cases Handled
| Scenario | Handling |
|----------|----------|
| 0% interest installment | Monthly Payment = Total / Installments (no interest component) |
| Fees included | User enters total payable amount (fees included) |
| Early payoff | Set remaining installments to 0, status to Completed |

---

## 4. Available Funds Formula

### Decision
```
Available Funds = Total Bank Assets (TWD)
                - Active Fixed Deposits Principal (TWD)
                - Unpaid Installment Balance (TWD)
```

### Rationale
- Fixed deposits are locked funds (non-liquid)
- Installments represent future payment obligations (reduce true liquidity)
- Currency conversion uses existing exchange rate service

### Important Notes
- Matured (but not closed) fixed deposits still count as committed (funds not yet released)
- Only installments with status `Active` are included

---

## 5. Entity Relationship Design

### Decision
```
User 1──* FixedDeposit (independent, bank name as text)
User 1──* CreditCard 1──* Installment
```

### Rationale
- Fixed deposits are independent (per clarification: not linked to BankAccount entity)
- Credit cards are containers for installments; enables per-card summary
- Installments always belong to a credit card (no orphan installments)

---

## 6. Currency Handling

### Decision
- Fixed deposits support multi-currency (same list as bank accounts)
- Credit card installments are always in TWD (Taiwan market typical)
- Available funds summary converts all to TWD using existing exchange rate service

### Rationale
- Fixed deposits may be in foreign currency (USD time deposits common)
- Credit card purchases in Taiwan are settled in TWD
- Consistent with existing TotalAssetsService pattern

---

## 7. UI Navigation Placement

### Decision
- Fixed deposits: New tab/section under "Bank Accounts" or separate menu item
- Credit cards & installments: New menu section "Credit Cards"
- Available funds summary: Displayed on existing total assets dashboard

### Rationale
- Fixed deposits are bank-related but distinct from regular accounts
- Credit cards are a new financial category
- Available funds is the primary value; should be prominently displayed

---

## 8. Soft Delete Strategy

### Decision
- Fixed deposits: No soft delete (use status transitions instead)
- Credit cards: Soft delete via `IsActive` flag (preserve installment history)
- Installments: No delete once created (mark as Completed or Cancelled)

### Rationale
- Financial records should maintain audit trail
- Deactivated credit cards may have historical installments
- Consistent with existing patterns (BankAccount uses IsActive)
