# API Contracts: UX Enhancements & Market Selection

**Date**: 2026-01-21
**Feature**: 003-ux-enhancements

## Overview

This document defines new and modified API endpoints for the 003-ux-enhancements feature.

---

## §1 Stock Split API

### GET /api/stock-splits

**Description**: Get all stock split records.

**Response**:
```json
{
  "items": [
    {
      "id": "uuid",
      "ticker": "NVDA",
      "splitDate": "2024-06-10",
      "splitRatio": 10.0,
      "description": "10-for-1 split",
      "createdAt": "2024-01-15T10:00:00Z",
      "updatedAt": "2024-01-15T10:00:00Z"
    }
  ]
}
```

### GET /api/stock-splits/{ticker}

**Description**: Get stock splits for a specific ticker.

**Parameters**:
- `ticker` (path): Stock ticker symbol

**Response**:
```json
{
  "items": [
    {
      "id": "uuid",
      "ticker": "NVDA",
      "splitDate": "2024-06-10",
      "splitRatio": 10.0,
      "description": "10-for-1 split"
    }
  ]
}
```

### POST /api/stock-splits

**Description**: Create a new stock split record.

**Request**:
```json
{
  "ticker": "NVDA",
  "splitDate": "2024-06-10",
  "splitRatio": 10.0,
  "description": "10-for-1 split"
}
```

**Response**: `201 Created`
```json
{
  "id": "uuid",
  "ticker": "NVDA",
  "splitDate": "2024-06-10",
  "splitRatio": 10.0,
  "description": "10-for-1 split",
  "createdAt": "2024-01-15T10:00:00Z"
}
```

**Errors**:
- `400 Bad Request`: Invalid split ratio (must be > 0)
- `409 Conflict`: Split already exists for this ticker/date

### PUT /api/stock-splits/{id}

**Description**: Update a stock split record.

**Request**:
```json
{
  "splitRatio": 10.0,
  "description": "Updated description"
}
```

**Response**: `200 OK` with updated entity

### DELETE /api/stock-splits/{id}

**Description**: Delete a stock split record.

**Response**: `204 No Content`

---

## §2 User Benchmark API

### GET /api/user-benchmarks

**Description**: Get current user's benchmark selections.

**Response**:
```json
{
  "items": [
    {
      "id": "uuid",
      "ticker": "VT",
      "market": 1,
      "marketName": "US",
      "displayName": "Vanguard Total World",
      "addedAt": "2024-01-15T10:00:00Z"
    }
  ]
}
```

### POST /api/user-benchmarks

**Description**: Add a custom benchmark stock.

**Request**:
```json
{
  "ticker": "VT",
  "market": 1,
  "displayName": "Vanguard Total World"
}
```

**Response**: `201 Created`
```json
{
  "id": "uuid",
  "ticker": "VT",
  "market": 1,
  "displayName": "Vanguard Total World",
  "addedAt": "2024-01-15T10:00:00Z"
}
```

**Errors**:
- `400 Bad Request`: Invalid market value
- `409 Conflict`: Benchmark already exists for this ticker/market

### DELETE /api/user-benchmarks/{id}

**Description**: Remove a custom benchmark.

**Response**: `204 No Content`

### GET /api/user-benchmarks/{id}/quote

**Description**: Get real-time quote for a user benchmark.

**Response**:
```json
{
  "ticker": "VT",
  "price": 105.23,
  "currency": "USD",
  "changePercent": 0.45,
  "fetchedAt": "2024-01-15T10:00:00Z",
  "isNonAccumulating": false,
  "dividendWarning": null
}
```

**Note**: If `isNonAccumulating` is true, include `dividendWarning` message.

---

## §3 Transaction API (Modified)

### POST /api/transactions

**Modified Request** - Add `market` field:
```json
{
  "portfolioId": "uuid",
  "ticker": "AAPL",
  "transactionDate": "2024-01-15",
  "transactionType": 0,
  "shares": 10,
  "pricePerShare": 185.50,
  "currency": "USD",
  "exchangeRate": 31.5,
  "fees": 1.99,
  "market": 1,  // NEW: 0=TW, 1=US, 2=UK, 3=EU
  "notes": "Optional notes"
}
```

**Validation**:
- `market`: Optional, defaults to auto-guess based on ticker format
- If provided, must be valid enum value (0-3)

### GET /api/transactions/{id}

**Modified Response** - Include `market` field:
```json
{
  "id": "uuid",
  "ticker": "AAPL",
  "market": 1,
  "marketName": "US",
  // ... other fields
}
```

---

## §4 Portfolio Summary API (Modified)

### GET /api/portfolios/{id}/summary

**Modified Response** - Position includes market from latest transaction:
```json
{
  "positions": [
    {
      "ticker": "AAPL",
      "market": 1,
      "marketName": "US",
      "totalShares": 100,
      "adjustedShares": 100,  // After split adjustments
      // ... other fields
    }
  ]
}
```

**Notes**:
- `market`: Derived from most recent transaction for this ticker
- `adjustedShares`: Calculated considering all applicable stock splits

---

## §5 Historical Performance API (Existing)

No changes needed. Dashboard chart uses existing endpoint:

### GET /api/performance/historical?portfolioId={id}

**Response** (unchanged):
```json
{
  "yearlySnapshots": [
    {
      "year": 2023,
      "startValue": 1000000,
      "endValue": 1150000,
      "returnPercentage": 15.0
    }
  ]
}
```

---

## §6 Error Response Format

All endpoints follow existing error format:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Split ratio must be greater than 0",
  "traceId": "00-abc123..."
}
```

---

## Phase 2 API Additions (US9-US17)

## §7 User Preferences API (US11)

### GET /api/user-preferences

**Description**: Get current user's preferences.

**Response**:
```json
{
  "ytdBenchmarkPreferences": "[\"SPY\", \"VTI\"]",
  "capeRegionPreferences": "[\"US\", \"TW\"]",
  "defaultPortfolioId": "uuid or null"
}
```

### PUT /api/user-preferences

**Description**: Update user preferences (partial update).

**Request**:
```json
{
  "ytdBenchmarkPreferences": "[\"SPY\", \"QQQ\"]",
  "capeRegionPreferences": "[\"US\"]",
  "defaultPortfolioId": "uuid or null"
}
```

**Response**: `200 OK` with updated preferences

**Notes**:
- All fields are optional - only provided fields are updated
- Preferences are stored as JSON strings

---

## §8 Transaction API (Modified - US9)

### POST /api/transactions

**Modified Request** - Add `currency` field:
```json
{
  "portfolioId": "uuid",
  "ticker": "AAPL",
  "transactionDate": "2024-01-15",
  "transactionType": 0,
  "shares": 10,
  "pricePerShare": 185.50,
  "market": 1,           // 0=TW, 1=US, 2=UK, 3=EU
  "currency": 2,         // NEW: 1=TWD, 2=USD, 3=GBP, 4=EUR
  "exchangeRate": 31.5,
  "fees": 1.99,
  "notes": "Optional notes"
}
```

**Auto-Detection**:
- If `currency` not provided: TW market → TWD (1), all others → USD (2)

### GET /api/transactions/{id}

**Modified Response** - Include `currency` field:
```json
{
  "id": "uuid",
  "ticker": "AAPL",
  "market": 1,
  "marketName": "US",
  "currency": 2,
  "currencyName": "USD",
  // ... other fields
}
```

---

## §9 Portfolio Summary API (Modified - US13)

### GET /api/portfolios/{id}/summary

**Modified Response** - Position uses (ticker, market) composite key:
```json
{
  "positions": [
    {
      "ticker": "WSML",
      "market": 1,
      "marketName": "US",
      "currency": 2,
      "currencyName": "USD",
      "totalShares": 100,
      "adjustedShares": 100
    },
    {
      "ticker": "WSML",
      "market": 2,
      "marketName": "UK",
      "currency": 3,
      "currencyName": "GBP",
      "totalShares": 50,
      "adjustedShares": 50
    }
  ]
}
```

**Notes**:
- Same ticker in different markets now displays as separate positions
- Position key is `(ticker, market)` not just `ticker`

---

## §10 Performance API (Modified - US10)

### GET /api/performance/xirr

**Modified Response** - Include calculation period warning:
```json
{
  "xirr": 0.1523,
  "calculationPeriodMonths": 2,
  "warningMessage": "計算期間少於 3 個月，XIRR 數值可能較不穩定",
  "isShortPeriod": true
}
```

**Notes**:
- `isShortPeriod`: true when calculation period < 3 months
- `warningMessage`: Localized warning message (may be null)
