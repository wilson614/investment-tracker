# Data Model: Performance Refinements

**Date**: 2026-01-23
**Feature**: 004-performance-refinements

## Overview

This document defines the data model changes required for performance refinements.

---

## New Entities

### 1. MonthlyNetWorthSnapshot

Stores cached month-end portfolio valuations for the historical chart.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| PortfolioId | Guid | FK, Index | Reference to Portfolio |
| Month | DateOnly | Index, Unique with PortfolioId | First day of the month (e.g., 2024-01-01) |
| TotalValueHome | decimal | Required | Total portfolio value in home currency |
| TotalContributions | decimal | Required | Cumulative contributions up to this month |
| PositionDetails | string (JSON) | Nullable | JSON array of position values for debugging |
| DataSource | string | Required | "Yahoo" / "Stooq" / "Mixed" / "Calculated" |
| CalculatedAt | DateTime | Required | When this snapshot was computed |
| CreatedAt | DateTime | Required | Record creation timestamp |
| UpdatedAt | DateTime | Required | Last update timestamp |

**Indexes**:
- `IX_MonthlyNetWorthSnapshot_PortfolioId_Month` (unique composite)

**Relationships**:
- Many-to-One with `Portfolio`

### 2. BenchmarkAnnualReturn

Stores cached annual total returns for benchmark ETFs.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| Symbol | string(20) | Required, Index | ETF symbol (e.g., "VWRA.L", "VT") |
| Year | int | Required, Index | Calendar year |
| TotalReturnPercent | decimal | Required | Annual total return including dividends |
| PriceReturnPercent | decimal | Nullable | Price-only return (for comparison) |
| DataSource | string | Required | "Yahoo" / "Calculated" |
| FetchedAt | DateTime | Required | When data was fetched |
| CreatedAt | DateTime | Required | Record creation timestamp |
| UpdatedAt | DateTime | Required | Last update timestamp |

**Indexes**:
- `IX_BenchmarkAnnualReturn_Symbol_Year` (unique composite)

---

## Modified Entities

### Portfolio (existing)

No schema changes. Used as parent for MonthlyNetWorthSnapshot.

### UserPreference (existing)

Add new preference key for currency mode.

| Preference Key | Value Type | Default | Description |
|----------------|------------|---------|-------------|
| `performance_currency_mode` | string | "source" | "source" or "home" for comparison display |

---

## DTO Changes

### YearPerformanceDto (modify)

Add Simple Return fields alongside existing XIRR fields.

```csharp
public record YearPerformanceDto
{
    // Existing fields
    public int Year { get; init; }
    public decimal? XirrPercentage { get; init; }
    public decimal? XirrPercentageHome { get; init; }
    // ... other existing fields

    // NEW: Simple Return fields
    public decimal? SimpleReturnSource { get; init; }
    public decimal? SimpleReturnHome { get; init; }
    public decimal StartValueSource { get; init; }
    public decimal StartValueHome { get; init; }
    public decimal EndValueSource { get; init; }
    public decimal EndValueHome { get; init; }
    public decimal NetContributionsSource { get; init; }
    public decimal NetContributionsHome { get; init; }
}
```

### StockPositionDto (modify)

Add source currency unrealized P&L.

```csharp
public record StockPositionDto
{
    // Existing fields
    public string Ticker { get; init; }
    public decimal TotalShares { get; init; }
    public decimal? UnrealizedPnlHome { get; init; }
    public decimal? UnrealizedPnlPercentage { get; init; }
    // ... other existing fields

    // NEW: Source currency P&L
    public decimal? UnrealizedPnlSource { get; init; }
    public decimal? UnrealizedPnlSourcePercentage { get; init; }
    public decimal TotalCostSource { get; init; }
    public decimal? CurrentValueSource { get; init; }
}
```

### MonthlyNetWorthDto (new)

Response DTO for monthly chart data.

```csharp
public record MonthlyNetWorthDto
{
    public string Month { get; init; }  // "2024-01" format
    public decimal Value { get; init; }
    public decimal Contributions { get; init; }
}

public record MonthlyNetWorthHistoryDto
{
    public List<MonthlyNetWorthDto> Data { get; init; }
    public string Currency { get; init; }
    public int TotalMonths { get; init; }
}
```

### BenchmarkReturnDto (modify)

Add source indicator.

```csharp
public record BenchmarkReturnDto
{
    public string Symbol { get; init; }
    public string Name { get; init; }
    public int Year { get; init; }
    public decimal ReturnPercent { get; init; }
    public string DataSource { get; init; }  // NEW: "Yahoo" / "Calculated"
}
```

---

## Database Migrations

### Migration: AddMonthlyNetWorthSnapshot

```sql
CREATE TABLE "MonthlyNetWorthSnapshots" (
    "Id" uuid PRIMARY KEY,
    "PortfolioId" uuid NOT NULL REFERENCES "Portfolios"("Id") ON DELETE CASCADE,
    "Month" date NOT NULL,
    "TotalValueHome" decimal(18,4) NOT NULL,
    "TotalContributions" decimal(18,4) NOT NULL,
    "PositionDetails" text NULL,
    "DataSource" varchar(20) NOT NULL,
    "CalculatedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX "IX_MonthlyNetWorthSnapshots_PortfolioId_Month"
ON "MonthlyNetWorthSnapshots" ("PortfolioId", "Month");
```

### Migration: AddBenchmarkAnnualReturn

```sql
CREATE TABLE "BenchmarkAnnualReturns" (
    "Id" uuid PRIMARY KEY,
    "Symbol" varchar(20) NOT NULL,
    "Year" int NOT NULL,
    "TotalReturnPercent" decimal(10,4) NOT NULL,
    "PriceReturnPercent" decimal(10,4) NULL,
    "DataSource" varchar(20) NOT NULL,
    "FetchedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX "IX_BenchmarkAnnualReturns_Symbol_Year"
ON "BenchmarkAnnualReturns" ("Symbol", "Year");
```

---

## Entity Relationship Diagram

```
┌─────────────┐       ┌──────────────────────────┐
│  Portfolio  │──1:N──│  MonthlyNetWorthSnapshot │
└─────────────┘       └──────────────────────────┘

┌──────────────────────┐
│  BenchmarkAnnualReturn │  (standalone, cached data)
└──────────────────────┘

┌─────────────┐       ┌─────────────────┐
│    User     │──1:N──│  UserPreference │
└─────────────┘       └─────────────────┘
```

---

## Validation Rules

### MonthlyNetWorthSnapshot
- `Month` must be first day of month
- `TotalValueHome` must be >= 0
- `DataSource` must be one of: "Yahoo", "Stooq", "Mixed", "Calculated"

### BenchmarkAnnualReturn
- `Year` must be >= 2000 and <= current year
- `Symbol` must match known benchmark symbols
- `TotalReturnPercent` reasonable range: -100% to +500%

---

## State Transitions

### MonthlyNetWorthSnapshot Lifecycle
1. **Created**: On first request for monthly chart
2. **Updated**: When portfolio transactions change (invalidate affected months)
3. **Deleted**: On portfolio deletion (cascade)

### Invalidation Triggers
- New transaction added → invalidate snapshots from transaction month onwards
- Transaction edited → invalidate snapshots from min(old, new) date onwards
- Transaction deleted → invalidate snapshots from transaction month onwards
