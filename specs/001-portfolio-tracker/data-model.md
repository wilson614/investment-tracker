# Data Model: Family Investment Portfolio Tracker

**Feature**: 001-portfolio-tracker
**Date**: 2026-01-06
**Status**: Complete

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ENTITY RELATIONSHIPS                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌──────────┐         ┌─────────────┐         ┌──────────────────┐        │
│   │   User   │ 1─────* │  Portfolio  │ 1─────* │ StockTransaction │        │
│   └──────────┘         └─────────────┘         └──────────────────┘        │
│        │                                                                    │
│        │ 1                                                                  │
│        │                                                                    │
│        ▼ *                                                                  │
│   ┌──────────────┐     ┌─────────────────────┐                             │
│   │CurrencyLedger│1──* │ CurrencyTransaction │                             │
│   └──────────────┘     └─────────────────────┘                             │
│                                                                             │
│   Legend: 1───* = One-to-Many                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Entities

### 1. User

Represents a family member with authentication credentials.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| Email | string(256) | Unique, Required | Login email |
| PasswordHash | string(512) | Required | Argon2id hashed password |
| DisplayName | string(100) | Required | User's display name |
| IsActive | bool | Default: true | Soft delete flag |
| CreatedAt | DateTime | Required | UTC timestamp |
| UpdatedAt | DateTime | Required | UTC timestamp |

**Relationships**:
- Has many `Portfolio`
- Has many `CurrencyLedger`
- Has many `RefreshToken`

**Indexes**:
- `IX_User_Email` (Unique)

---

### 2. Portfolio

Contains holdings and transactions for a specific user.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK → User, Required | Owner |
| Name | string(100) | Required | Portfolio name (e.g., "主要投資帳戶") |
| Description | string(500) | Optional | Notes |
| BaseCurrency | string(3) | Required, Default: "USD" | Source currency for this portfolio |
| HomeCurrency | string(3) | Required, Default: "TWD" | Home currency for reporting |
| IsActive | bool | Default: true | Soft delete flag |
| CreatedAt | DateTime | Required | UTC timestamp |
| UpdatedAt | DateTime | Required | UTC timestamp |

**Relationships**:
- Belongs to `User`
- Has many `StockTransaction`

**Indexes**:
- `IX_Portfolio_UserId`

**Validation Rules**:
- BaseCurrency and HomeCurrency must be valid ISO 4217 codes

---

### 3. StockTransaction

Records buy/sell activity for stocks/ETFs.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| PortfolioId | Guid | FK → Portfolio, Required | Parent portfolio |
| TransactionDate | DateTime | Required | Trade date |
| Ticker | string(20) | Required | Stock/ETF symbol (e.g., "VWRA", "VTI") |
| TransactionType | enum | Required | Buy, Sell, Split, Adjustment |
| Shares | decimal(18,4) | Required | Number of shares (supports fractional) |
| PricePerShare | decimal(18,4) | Required | Price in source currency |
| ExchangeRate | decimal(18,6) | Required | Source to Home currency rate |
| Fees | decimal(18,2) | Default: 0 | Transaction fees in source currency |
| FundSource | enum | Optional | None, CurrencyLedger |
| CurrencyLedgerId | Guid? | FK → CurrencyLedger | If FundSource = CurrencyLedger |
| Notes | string(500) | Optional | User notes |
| IsDeleted | bool | Default: false | Soft delete flag |
| CreatedAt | DateTime | Required | UTC timestamp |
| UpdatedAt | DateTime | Required | UTC timestamp |

**Calculated Fields** (computed, not stored):
- `TotalCostSource` = (Shares × PricePerShare) + Fees
- `TotalCostHome` = TotalCostSource × ExchangeRate

**Relationships**:
- Belongs to `Portfolio`
- Optionally linked to `CurrencyLedger` (for fund source tracking)

**Indexes**:
- `IX_StockTransaction_PortfolioId`
- `IX_StockTransaction_Ticker`
- `IX_StockTransaction_TransactionDate`

**Validation Rules**:
- Shares > 0 for Buy; Shares > 0 for Sell (system validates sufficient balance)
- ExchangeRate > 0
- Fees >= 0
- For Sell: Cannot sell more shares than currently held (checked at application layer)

---

### 4. CurrencyLedger

Tracks foreign currency holdings with weighted average cost.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK → User, Required | Owner |
| CurrencyCode | string(3) | Required | Currency (e.g., "USD") |
| Name | string(100) | Required | Ledger name (e.g., "美元帳戶") |
| HomeCurrency | string(3) | Required, Default: "TWD" | Home currency for cost basis |
| IsActive | bool | Default: true | Soft delete flag |
| CreatedAt | DateTime | Required | UTC timestamp |
| UpdatedAt | DateTime | Required | UTC timestamp |

**Calculated Fields** (derived from transactions):
- `Balance` = Sum of all transaction amounts
- `WeightedAverageRate` = TotalCostHome / Balance
- `TotalValueHome` = Balance × Current market rate

**Relationships**:
- Belongs to `User`
- Has many `CurrencyTransaction`

**Indexes**:
- `IX_CurrencyLedger_UserId`
- `IX_CurrencyLedger_UserId_CurrencyCode` (Unique per user)

---

### 5. CurrencyTransaction

Records currency exchange, interest, and spend events.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| CurrencyLedgerId | Guid | FK → CurrencyLedger, Required | Parent ledger |
| TransactionDate | DateTime | Required | Transaction date |
| TransactionType | enum | Required | ExchangeBuy, ExchangeSell, Interest, Spend |
| ForeignAmount | decimal(18,4) | Required | Amount in foreign currency |
| HomeAmount | decimal(18,2) | Conditional | Amount in home currency (for Exchange types) |
| ExchangeRate | decimal(18,6) | Conditional | Rate used (for Exchange types) |
| RelatedStockTransactionId | Guid? | FK → StockTransaction | If Type = Spend (links to stock purchase) |
| Notes | string(500) | Optional | User notes |
| IsDeleted | bool | Default: false | Soft delete flag |
| CreatedAt | DateTime | Required | UTC timestamp |
| UpdatedAt | DateTime | Required | UTC timestamp |

**Transaction Type Rules**:

| Type | ForeignAmount | HomeAmount | ExchangeRate | Effect on Balance | Effect on Avg Rate |
|------|---------------|------------|--------------|-------------------|-------------------|
| ExchangeBuy | + (incoming) | + (spent) | Required | ↑ Increase | Recalculate |
| ExchangeSell | - (outgoing) | + (received) | Required | ↓ Decrease | Unchanged |
| Interest | + (incoming) | 0 (default) | Optional | ↑ Increase | ↓ Decrease (0 cost) |
| Spend | - (outgoing) | N/A | N/A | ↓ Decrease | Unchanged |

**Relationships**:
- Belongs to `CurrencyLedger`
- Optionally linked to `StockTransaction` (for Spend type)

**Indexes**:
- `IX_CurrencyTransaction_CurrencyLedgerId`
- `IX_CurrencyTransaction_TransactionDate`

**Validation Rules**:
- ForeignAmount > 0 (absolute value; sign determined by type)
- For ExchangeBuy/ExchangeSell: HomeAmount > 0, ExchangeRate > 0
- For Spend: Cannot exceed current balance

---

### 6. RefreshToken

Stores JWT refresh tokens for authentication.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| UserId | Guid | FK → User, Required | Token owner |
| Token | string(512) | Required, Unique | Hashed token value |
| ExpiresAt | DateTime | Required | Expiration timestamp |
| CreatedAt | DateTime | Required | UTC timestamp |
| RevokedAt | DateTime? | Optional | If revoked |
| ReplacedByTokenId | Guid? | FK → RefreshToken | Token rotation chain |

**Indexes**:
- `IX_RefreshToken_Token` (Unique)
- `IX_RefreshToken_UserId`

---

## Enumerations

### TransactionType (StockTransaction)
```csharp
public enum TransactionType
{
    Buy = 1,
    Sell = 2,
    Split = 3,      // Stock split adjustment
    Adjustment = 4  // Manual correction
}
```

### CurrencyTransactionType
```csharp
public enum CurrencyTransactionType
{
    ExchangeBuy = 1,   // Home → Foreign (e.g., TWD → USD)
    ExchangeSell = 2,  // Foreign → Home (e.g., USD → TWD)
    Interest = 3,      // Bank interest received
    Spend = 4          // Used for stock purchase
}
```

### FundSource
```csharp
public enum FundSource
{
    None = 0,           // Not tracked / external
    CurrencyLedger = 1  // Linked to CurrencyLedger
}
```

---

## Value Objects

### Money
```csharp
public record Money(decimal Amount, string CurrencyCode)
{
    public Money ConvertTo(string targetCurrency, decimal rate)
        => new(Amount * rate, targetCurrency);
}
```

### ExchangeRateValue
```csharp
public record ExchangeRateValue(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTime AsOf);
```

---

## Database Constraints Summary

| Table | Constraint | Type | Description |
|-------|------------|------|-------------|
| User | IX_User_Email | Unique | One account per email |
| Portfolio | FK_Portfolio_User | Foreign Key | Cascade delete |
| CurrencyLedger | IX_CurrencyLedger_UserId_CurrencyCode | Unique | One ledger per currency per user |
| StockTransaction | FK_StockTransaction_Portfolio | Foreign Key | Cascade delete |
| CurrencyTransaction | FK_CurrencyTransaction_CurrencyLedger | Foreign Key | Cascade delete |

---

## Soft Delete Strategy

All financial records use soft deletes to maintain audit trails:

- `IsDeleted` / `IsActive` flags
- EF Core global query filters exclude deleted records by default
- Explicit `IgnoreQueryFilters()` for admin/audit views

```csharp
// Global filter example
modelBuilder.Entity<StockTransaction>()
    .HasQueryFilter(t => !t.IsDeleted);
```

---

## Audit Timestamps

All entities include:
- `CreatedAt`: Set on insert (UTC)
- `UpdatedAt`: Set on insert and every update (UTC)

Implemented via EF Core `SaveChanges` override:

```csharp
public override Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
    {
        if (entry.State == EntityState.Added)
            entry.Entity.CreatedAt = DateTime.UtcNow;

        if (entry.State is EntityState.Added or EntityState.Modified)
            entry.Entity.UpdatedAt = DateTime.UtcNow;
    }
    return base.SaveChangesAsync(ct);
}
```
