# Research: Bank Account Enhancements

**Feature**: 006-bank-account-enhancements
**Date**: 2026-02-06

## Research Topics

### 1. Multi-Currency Bank Account Support

**Decision**: Use ISO 4217 currency codes stored as string field on BankAccount entity

**Rationale**:
- String storage (3-char code) is simple and widely understood
- No need for separate Currency table for initial 7 currencies
- Consistent with existing ExchangeRate entity which uses string currency codes
- Frontend can use `Intl.NumberFormat` with currency codes for formatting

**Alternatives Considered**:
- Separate Currency lookup table: Rejected - over-engineering for 7 fixed currencies
- Integer enum: Rejected - less readable in database, harder to extend

**Implementation Notes**:
- Default value for existing accounts: "TWD" (via migration)
- Supported currencies: TWD, USD, EUR, JPY, CNY, GBP, AUD
- Currency selection on form with dropdown

---

### 2. Exchange Rate Integration for Bank Assets

**Decision**: Reuse existing `IExchangeRateRepository.GetLatestRateAsync()` for currency conversion

**Rationale**:
- Exchange rate infrastructure already exists for portfolio calculations
- `ExchangeRate` entity stores rates with timestamps
- TotalAssetsService can query rates for each foreign currency bank account

**Alternatives Considered**:
- Cache rates at BankAccount level: Rejected - stale data risk, unnecessary complexity
- Real-time API calls: Rejected - already have rate storage, adds external dependency

**Implementation Notes**:
- When exchange rate unavailable: use last known rate + show stale indicator
- Convert all bank accounts to TWD for total calculation
- Store original currency amount, display converted value separately

---

### 3. Fund Allocation Data Model

**Decision**: New `FundAllocation` entity with Purpose enum and Amount in TWD

**Rationale**:
- Separate entity allows multiple allocations per user
- Amount stored in TWD simplifies comparison with total bank assets (already in TWD)
- Purpose as enum prevents arbitrary strings, ensures consistency

**Alternatives Considered**:
- JSON blob on User entity: Rejected - harder to query, validate, audit
- Percentage-based allocation: Rejected - absolute amounts clearer for user's mental model

**Implementation Notes**:
- FundAllocation: Id, UserId, Purpose (enum), Amount (decimal), CreatedAt, UpdatedAt
- AllocationPurpose enum: EmergencyFund, FamilyDeposit, General, Savings
- Over-allocation validation at API level before save

---

### 4. Historical Performance Multi-Currency Generalization

**Decision**: Replace `GetUsdToTwdRate` with `GetSourceToHomeRate(baseCurrency, homeCurrency)`

**Rationale**:
- Current implementation hardcodes USD as source currency
- Portfolios can have BaseCurrency = TWD, where no conversion needed (rate = 1.0)
- Generalized function supports any currency pair

**Alternatives Considered**:
- Separate code paths for TWD vs USD portfolios: Rejected - code duplication, maintenance burden
- Always convert to USD first then to TWD: Rejected - unnecessary intermediate step, precision loss

**Implementation Notes**:
- When baseCurrency == homeCurrency (e.g., TWD portfolio with TWD home): return 1.0
- When baseCurrency == "USD" and homeCurrency == "TWD": existing logic
- When baseCurrency == "TWD" and homeCurrency == "TWD": rate = 1.0, no conversion

---

### 5. Currency Display Formatting

**Decision**: Create unified `formatCurrency(amount, currencyCode)` utility using `Intl.NumberFormat`

**Rationale**:
- JavaScript's `Intl.NumberFormat` handles currency symbols, decimal places, thousand separators
- Single source of truth prevents inconsistent formatting
- Already in TypeScript, strongly typed

**Alternatives Considered**:
- Manual formatting with switch/case: Rejected - error-prone, doesn't handle locales
- Third-party library (currency.js): Rejected - Intl.NumberFormat is built-in, sufficient

**Implementation Notes**:
- Location: `frontend/src/utils/currency.ts`
- Function signature: `formatCurrency(amount: number, currency: string = 'TWD'): string`
- Use locale `zh-TW` for consistent formatting
- Handle edge cases: null/undefined → return empty string or "—"

---

### 6. Interest Cap Display Logic

**Decision**: Use explicit null check (`interestCap != null`) instead of truthy check

**Rationale**:
- Current bug: `interestCap ? ...` treats 0 as falsy
- Explicit null check distinguishes between: null (無上限), 0 (cap is zero), positive value

**Alternatives Considered**:
- Use -1 for "no cap": Rejected - magic number, changes data model
- Separate boolean field `hasInterestCap`: Rejected - redundant, over-engineering

**Implementation Notes**:
- File: `BankAccountCard.tsx:63` (per CLAUDE.md)
- Change: `account.interestCap ?` → `account.interestCap != null ?`
- Display logic: null → "無上限", 0 → "NT$ 0", positive → formatted amount

---

## Summary

All research topics resolved. No external dependencies required. Implementation follows existing patterns in the codebase.

| Topic | Decision | Risk |
|-------|----------|------|
| Multi-currency storage | String ISO 4217 codes | Low |
| Exchange rate integration | Reuse existing service | Low |
| Fund allocation model | New entity with enum | Low |
| Historical performance | Generalize rate function | Medium (requires careful testing) |
| Currency formatting | Intl.NumberFormat utility | Low |
| Interest cap fix | Explicit null check | Low |
