# API Changes: Performance Refinements

**Date**: 2026-01-23
**Feature**: 004-performance-refinements

## Overview

This document defines the API endpoint changes for performance refinements.

---

## Modified Endpoints

### 1. GET /api/portfolios/{id}/performance/year

**Change**: Response now includes Simple Return fields alongside XIRR.

**Request**: No change

**Response** (modified):
```json
{
  "year": 2024,
  "isYtd": false,

  // Existing XIRR fields (kept for backwards compatibility)
  "xirrPercentage": 12.5,
  "xirrPercentageHome": 15.2,
  "cashFlowCount": 15,

  // NEW: Simple Return fields
  "simpleReturnSource": 10.3,
  "simpleReturnHome": 13.1,
  "startValueSource": 50000.00,
  "startValueHome": 1550000,
  "endValueSource": 58000.00,
  "endValueHome": 1820000,
  "netContributionsSource": 3000.00,
  "netContributionsHome": 95000,

  // Existing fields
  "totalValueSource": 58000.00,
  "totalValueHome": 1820000,
  "positionCount": 8
}
```

### 2. GET /api/portfolios/{id}/summary

**Change**: Position objects now include source currency P&L.

**Response** (position object modified):
```json
{
  "portfolio": { ... },
  "positions": [
    {
      "ticker": "VT",
      "market": 1,
      "totalShares": 100.0,
      "averageCostPerShareSource": 95.50,
      "totalCostSource": 9550.00,
      "totalCostHome": 295000,

      // Existing home currency P&L
      "currentValueHome": 320000,
      "unrealizedPnlHome": 25000,
      "unrealizedPnlPercentage": 8.47,

      // NEW: Source currency P&L
      "currentValueSource": 10200.00,
      "unrealizedPnlSource": 650.00,
      "unrealizedPnlSourcePercentage": 6.81
    }
  ]
}
```

---

## New Endpoints

### 3. GET /api/portfolios/{id}/performance/monthly

**Purpose**: Get monthly net worth history for chart display.

**Notes**:
- Includes current month "as-of today" valuation (prices and FX use nearest trading day on or before today)
- Contributions represent cumulative net contributions (Buy - Sell) in home currency
- Valuation uses month-end FX rate (nearest trading day on or before valuation date)

**Request**:
```
GET /api/portfolios/{portfolioId}/performance/monthly
```

**Query Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| fromMonth | string | No | Start month (YYYY-MM format), defaults to first transaction |
| toMonth | string | No | End month (YYYY-MM format), defaults to current month |

**Response**:
```json
{
  "data": [
    {
      "month": "2023-01",
      "value": 1250000,
      "contributions": 1100000
    },
    {
      "month": "2023-02",
      "value": 1280000,
      "contributions": 1100000
    },
    {
      "month": "2023-03",
      "value": 1350000,
      "contributions": 1150000
    }
  ],
  "currency": "TWD",
  "totalMonths": 24,
  "dataSource": "Mixed"
}
```

**Error Responses**:
| Status | Condition |
|--------|-----------|
| 404 | Portfolio not found |
| 403 | User doesn't own portfolio |

### 4. GET /api/market-data/benchmark-returns/{year}

**Purpose**: Get benchmark annual returns for a specific year.

**Request**:
```
GET /api/market-data/benchmark-returns/2023
```

**Response**:
```json
{
  "year": 2023,
  "benchmarks": [
    {
      "symbol": "VWRA.L",
      "name": "All Country",
      "returnPercent": 22.15,
      "dataSource": "Yahoo"
    },
    {
      "symbol": "VT",
      "name": "US Large",
      "returnPercent": 26.03,
      "dataSource": "Yahoo"
    },
    {
      "symbol": "0050.TW",
      "name": "Taiwan 0050",
      "returnPercent": 28.50,
      "dataSource": "Calculated"
    }
  ]
}
```

**Error Responses**:
| Status | Condition |
|--------|-----------|
| 400 | Invalid year (< 2000 or > current year) |

### 5. POST /api/market-data/benchmark-returns/{year}/refresh

**Purpose**: Force refresh benchmark returns from Yahoo.

**Request**:
```
POST /api/market-data/benchmark-returns/2023/refresh
```

**Response**: Same as GET endpoint above.

---

## Authentication Endpoints (Verification)

### 6. POST /api/auth/refresh (existing, verify behavior)

**Purpose**: Exchange refresh token for new access token.

**Request**:
```json
{
  "refreshToken": "base64-encoded-refresh-token"
}
```

**Response**:
```json
{
  "accessToken": "jwt-access-token",
  "refreshToken": "new-refresh-token",
  "expiresAt": "2026-01-23T14:00:00Z",
  "user": {
    "id": "guid",
    "email": "user@example.com",
    "displayName": "User Name"
  }
}
```

**Error Responses**:
| Status | Condition |
|--------|-----------|
| 401 | Invalid or expired refresh token |

---

## Configuration Changes

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `Jwt__AccessTokenExpirationMinutes` | 120 | Access token validity (changed from 15) |
| `Jwt__RefreshTokenExpirationDays` | 7 | Refresh token validity (unchanged) |

---

## Frontend API Client Changes

### api.ts Updates

1. **Add token refresh logic to `fetchApi`**:
```typescript
async function fetchApi<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  let response = await fetchWithToken(endpoint, options);

  if (response.status === 401) {
    const refreshed = await tryRefreshToken();
    if (refreshed) {
      response = await fetchWithToken(endpoint, options);
    } else {
      clearAuthAndRedirect();
      throw createApiError(401, 'Session expired');
    }
  }

  // ... rest of error handling
}
```

2. **Add new API methods**:
```typescript
export const portfolioApi = {
  // ... existing methods

  getMonthlyNetWorth: (portfolioId: string, fromMonth?: string, toMonth?: string) =>
    fetchApi<MonthlyNetWorthHistory>(`/portfolios/${portfolioId}/performance/monthly`),
};

export const marketDataApi = {
  // ... existing methods

  getBenchmarkReturns: (year: number) =>
    fetchApi<BenchmarkReturnsResponse>(`/market-data/benchmark-returns/${year}`),

  refreshBenchmarkReturns: (year: number) =>
    fetchApi<BenchmarkReturnsResponse>(`/market-data/benchmark-returns/${year}/refresh`, {
      method: 'POST',
    }),
};
```

---

## Backwards Compatibility

| Change | Compatibility |
|--------|---------------|
| YearPerformance new fields | ✅ Additive - old clients ignore new fields |
| Position new fields | ✅ Additive - old clients ignore new fields |
| New monthly endpoint | ✅ New endpoint - no impact on existing |
| New benchmark endpoint | ✅ New endpoint - no impact on existing |
| Token expiration change | ✅ Configuration only - no API change |
