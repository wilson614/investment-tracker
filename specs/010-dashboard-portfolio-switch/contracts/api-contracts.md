# API Contracts: Dashboard & Portfolio Switching Overhaul

**Feature**: 010-dashboard-portfolio-switch
**Date**: 2026-02-11
**Base URL**: `/api`

## New Endpoints

### 1. POST /api/portfolios/aggregate/xirr

Calculate aggregate XIRR across all user portfolios.

**Request Body**:
```json
{
  "currentPrices": {
    "AAPL": { "price": 185.50, "exchangeRate": 31.5 },
    "2330.TW": { "price": 580.00, "exchangeRate": 1.0 }
  }
}
```

**Response 200**:
```json
{
  "xirr": 0.1234,
  "xirrPercentage": 12.34,
  "cashFlowCount": 45,
  "asOfDate": "2026-02-11T00:00:00Z",
  "earliestTransactionDate": "2020-03-15T00:00:00Z",
  "missingExchangeRates": []
}
```

**Behavior**:
- Collects all transactions from all user portfolios.
- Combines as single cash flow series (Buy = negative, Sell = positive).
- Current total value across all portfolios (using provided prices) as final cash flow.
- Returns same DTO shape as existing per-portfolio XIRR endpoint.

---

### 2. GET /api/portfolios/aggregate/performance/years

Get union of available years across all user portfolios.

**Response 200**:
```json
{
  "years": [2026, 2025, 2024, 2023, 2022, 2021, 2020],
  "earliestYear": 2020,
  "currentYear": 2026
}
```

**Behavior**:
- Finds earliest transaction year across all portfolios.
- Returns descending year list from earliest to current year.
- Same DTO shape as existing per-portfolio years endpoint.

---

### 3. POST /api/portfolios/aggregate/performance/year

Calculate aggregate annual performance across all user portfolios.

**Request Body**:
```json
{
  "year": 2025,
  "yearEndPrices": {
    "AAPL": { "price": 190.00, "exchangeRate": 31.5, "date": "2025-12-31" }
  },
  "yearStartPrices": {
    "AAPL": { "price": 170.00, "exchangeRate": 30.8, "date": "2025-01-02" }
  }
}
```

**Response 200**:
```json
{
  "year": 2025,
  "xirrPercentage": 15.2,
  "totalReturnPercentage": 14.8,
  "modifiedDietzPercentage": 14.5,
  "timeWeightedReturnPercentage": 13.9,
  "startValueHome": 2500000,
  "endValueHome": 2870000,
  "netContributionsHome": 120000,
  "cashFlowCount": 28,
  "transactionCount": 22,
  "earliestTransactionDateInYear": "2025-01-15T00:00:00Z",
  "missingPrices": []
}
```

**Behavior**:
- Combines transactions from all portfolios for the specified year.
- Calculates aggregate starting value (sum of all portfolio starting values).
- Calculates aggregate ending value (sum of all portfolio ending values).
- Computes XIRR, Modified Dietz, and TWR using combined data.
- Returns consolidated missing prices from all portfolios.
- Same DTO shape as existing per-portfolio year performance endpoint.

---

## Existing Endpoints Used (No Changes)

These endpoints are called per-portfolio by the frontend for aggregate data assembly:

| Endpoint | Aggregate Usage |
|----------|----------------|
| `GET /api/portfolios` | Get all portfolios for the user |
| `GET/POST /api/portfolios/{id}/summary` | Called per portfolio, merged on frontend |
| `GET /api/portfolios/{id}/performance/monthly` | Called per portfolio, summed by month on frontend |
| `GET /api/stocktransactions?portfolioId={id}` | Called per portfolio, merged + sorted on frontend |

## Error Responses

All new endpoints follow existing error conventions:

| Status | Condition |
|--------|-----------|
| 401 | Unauthorized (no valid JWT) |
| 404 | User has no portfolios |
| 400 | Invalid request body (malformed prices) |
