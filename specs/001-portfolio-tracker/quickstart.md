# Quickstart: Family Investment Portfolio Tracker

**Feature**: 001-portfolio-tracker
**Date**: 2026-01-06

This guide walks through the happy path for the core user journeys.

---

## Prerequisites

1. Docker and Docker Compose installed
2. Git repository cloned
3. `.env` file configured (see Environment Setup)

---

## Environment Setup

Create `.env` file in project root:

```bash
# Database
DB_USER=investmenttracker
DB_PASSWORD=your_secure_password_here
DB_NAME=investmenttracker

# JWT
JWT_SECRET=your_256_bit_secret_key_here_at_least_32_chars

# Backend
ASPNETCORE_ENVIRONMENT=Development

# Frontend (optional for local dev)
VITE_API_URL=http://localhost:5000/api/v1
```

---

## Start Services

```bash
# From project root
docker-compose up -d

# Verify all services are running
docker-compose ps

# Expected output:
# NAME                    STATUS
# investmenttracker-db    Up
# investmenttracker-api   Up
# investmenttracker-web   Up
```

Access points:
- **Frontend**: http://localhost:80
- **Backend API**: http://localhost:5000/api/v1
- **Swagger UI**: http://localhost:5000/swagger

---

## User Journey 1: Register and Login

### Step 1.1: Register a new user

```bash
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@family.com",
    "password": "SecurePass123!",
    "displayName": "Alice"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440001",
  "email": "alice@family.com",
  "displayName": "Alice"
}
```

### Step 1.2: Login

```bash
curl -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@family.com",
    "password": "SecurePass123!"
  }'
```

Expected response (200 OK):
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2g...",
  "expiresAt": "2026-01-06T12:15:00Z",
  "user": {
    "id": "550e8400-e29b-41d4-a716-446655440001",
    "email": "alice@family.com",
    "displayName": "Alice"
  }
}
```

**Save the accessToken for subsequent requests.**

---

## User Journey 2: Create Portfolio and Record Transactions

### Step 2.1: Create a portfolio

```bash
export TOKEN="your_access_token_here"

curl -X POST http://localhost:5000/api/v1/portfolios \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "主要投資帳戶",
    "description": "美股 ETF 長期投資",
    "baseCurrency": "USD",
    "homeCurrency": "TWD"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440010",
  "name": "主要投資帳戶",
  "description": "美股 ETF 長期投資",
  "baseCurrency": "USD",
  "homeCurrency": "TWD",
  "createdAt": "2026-01-06T10:00:00Z",
  "updatedAt": "2026-01-06T10:00:00Z"
}
```

### Step 2.2: Record a buy transaction

```bash
export PORTFOLIO_ID="550e8400-e29b-41d4-a716-446655440010"

curl -X POST "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID/transactions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "transactionDate": "2026-01-05",
    "ticker": "VWRA",
    "transactionType": "Buy",
    "shares": 10.5,
    "pricePerShare": 120.50,
    "exchangeRate": 31.5,
    "fees": 5.00,
    "fundSource": "None",
    "notes": "定期定額投資"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440020",
  "portfolioId": "550e8400-e29b-41d4-a716-446655440010",
  "transactionDate": "2026-01-05",
  "ticker": "VWRA",
  "transactionType": "Buy",
  "shares": 10.5000,
  "pricePerShare": 120.5000,
  "exchangeRate": 31.500000,
  "fees": 5.00,
  "fundSource": "None",
  "currencyLedgerId": null,
  "totalCostSource": 1270.25,
  "totalCostHome": 40012.88,
  "notes": "定期定額投資",
  "createdAt": "2026-01-06T10:05:00Z",
  "updatedAt": "2026-01-06T10:05:00Z"
}
```

**Verification**: Total Cost (Source) = (10.5 × 120.50) + 5 = $1,270.25 ✓

---

## User Journey 3: Currency Ledger Management

### Step 3.1: Create a currency ledger

```bash
curl -X POST http://localhost:5000/api/v1/currency-ledgers \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "currencyCode": "USD",
    "name": "美元帳戶",
    "homeCurrency": "TWD"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440030",
  "currencyCode": "USD",
  "name": "美元帳戶",
  "homeCurrency": "TWD",
  "balance": 0.0000,
  "weightedAverageRate": 0.000000,
  "totalCostHome": 0.00,
  "createdAt": "2026-01-06T10:10:00Z",
  "updatedAt": "2026-01-06T10:10:00Z"
}
```

### Step 3.2: Exchange TWD to USD

```bash
export LEDGER_ID="550e8400-e29b-41d4-a716-446655440030"

curl -X POST "http://localhost:5000/api/v1/currency-ledgers/$LEDGER_ID/transactions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "transactionDate": "2026-01-05",
    "transactionType": "ExchangeBuy",
    "foreignAmount": 3200.00,
    "homeAmount": 100000.00,
    "exchangeRate": 31.25,
    "notes": "換匯"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440040",
  "currencyLedgerId": "550e8400-e29b-41d4-a716-446655440030",
  "transactionDate": "2026-01-05",
  "transactionType": "ExchangeBuy",
  "foreignAmount": 3200.0000,
  "homeAmount": 100000.00,
  "exchangeRate": 31.250000,
  "realizedPnlHome": null,
  "notes": "換匯",
  "createdAt": "2026-01-06T10:15:00Z",
  "updatedAt": "2026-01-06T10:15:00Z"
}
```

### Step 3.3: Verify ledger balance

```bash
curl "http://localhost:5000/api/v1/currency-ledgers/$LEDGER_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440030",
  "currencyCode": "USD",
  "name": "美元帳戶",
  "homeCurrency": "TWD",
  "balance": 3200.0000,
  "weightedAverageRate": 31.250000,
  "totalCostHome": 100000.00,
  "createdAt": "2026-01-06T10:10:00Z",
  "updatedAt": "2026-01-06T10:15:00Z"
}
```

---

## User Journey 4: Buy Stock Using Currency Ledger

### Step 4.1: Create stock transaction with currency ledger as fund source

```bash
curl -X POST "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID/transactions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "transactionDate": "2026-01-06",
    "ticker": "VTI",
    "transactionType": "Buy",
    "shares": 10.0000,
    "pricePerShare": 200.00,
    "exchangeRate": 31.50,
    "fees": 5.00,
    "fundSource": "CurrencyLedger",
    "currencyLedgerId": "550e8400-e29b-41d4-a716-446655440030",
    "notes": "使用美元帳戶購買"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440050",
  "portfolioId": "550e8400-e29b-41d4-a716-446655440010",
  "transactionDate": "2026-01-06",
  "ticker": "VTI",
  "transactionType": "Buy",
  "shares": 10.0000,
  "pricePerShare": 200.0000,
  "exchangeRate": 31.500000,
  "fees": 5.00,
  "fundSource": "CurrencyLedger",
  "currencyLedgerId": "550e8400-e29b-41d4-a716-446655440030",
  "totalCostSource": 2005.00,
  "totalCostHome": 63157.50,
  "notes": "使用美元帳戶購買"
}
```

### Step 4.2: Verify currency ledger was debited

```bash
curl "http://localhost:5000/api/v1/currency-ledgers/$LEDGER_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "balance": 1195.0000,
  "weightedAverageRate": 31.250000,
  "totalCostHome": 37343.75
}
```

**Verification**:
- Original balance: $3,200
- Spent: $2,005 (10 × 200 + 5)
- New balance: $3,200 - $2,005 = $1,195 ✓
- Weighted average rate unchanged: 31.25 ✓

---

## User Journey 5: View Portfolio Performance

### Step 5.1: Get portfolio summary

```bash
curl "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "portfolio": {
    "id": "550e8400-e29b-41d4-a716-446655440010",
    "name": "主要投資帳戶"
  },
  "positions": [
    {
      "ticker": "VWRA",
      "totalShares": 10.5000,
      "averageCostSource": 121.4524,
      "averageCostHome": 3810.75,
      "totalCostHome": 40012.88,
      "currentPrice": null,
      "currentValueHome": null,
      "unrealizedPnlHome": null
    },
    {
      "ticker": "VTI",
      "totalShares": 10.0000,
      "averageCostSource": 200.5000,
      "averageCostHome": 6315.75,
      "totalCostHome": 63157.50,
      "currentPrice": null,
      "currentValueHome": null,
      "unrealizedPnlHome": null
    }
  ],
  "totalValueHome": null,
  "totalCostHome": 103170.38,
  "unrealizedPnlHome": null,
  "xirr": null
}
```

### Step 5.2: Calculate XIRR with current prices

```bash
curl "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID/xirr?currentPrice[VWRA]=130&currentPrice[VTI]=210" \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "xirr": 0.1523,
  "annualizedReturn": "15.23%"
}
```

---

## User Journey 6: Sell Stock and View Realized PnL

### Step 6.1: Record a sell transaction

```bash
curl -X POST "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID/transactions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "transactionDate": "2026-01-06",
    "ticker": "VTI",
    "transactionType": "Sell",
    "shares": 5.0000,
    "pricePerShare": 210.00,
    "exchangeRate": 32.00,
    "fees": 5.00,
    "notes": "部分獲利了結"
  }'
```

Expected response (201 Created):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440060",
  "ticker": "VTI",
  "transactionType": "Sell",
  "shares": 5.0000,
  "pricePerShare": 210.0000,
  "exchangeRate": 32.000000,
  "fees": 5.00,
  "totalCostSource": 1045.00,
  "totalCostHome": 33440.00
}
```

### Step 6.2: View updated position

```bash
curl "http://localhost:5000/api/v1/portfolios/$PORTFOLIO_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Expected VTI position:
```json
{
  "ticker": "VTI",
  "totalShares": 5.0000,
  "averageCostSource": 200.5000,
  "totalCostHome": 31578.75
}
```

**Realized PnL Calculation**:
- Proceeds (Home): 5 × 210 × 32 - 5 = $33,595 TWD
- Cost Basis (Home): 5 × 6,315.75 = $31,578.75 TWD
- **Realized Profit**: $33,595 - $31,578.75 = **$2,016.25 TWD** ✓

---

## Troubleshooting

### Common Issues

1. **401 Unauthorized**
   - Token expired. Call `/auth/refresh` with your refresh token.

2. **400 Bad Request: Insufficient shares**
   - Cannot sell more shares than you own. Check current position.

3. **400 Bad Request: Insufficient currency balance**
   - Currency ledger doesn't have enough funds for the stock purchase.

4. **Database connection error**
   - Ensure PostgreSQL container is running: `docker-compose ps`
   - Check connection string in `.env`

### Logs

```bash
# View all logs
docker-compose logs -f

# View only backend logs
docker-compose logs -f backend
```

---

## Next Steps

- Run `/speckit.tasks` to generate implementation tasks
- Begin with Phase 1 (Setup) and Phase 2 (Foundational)
- Implement User Story 1 (P1) first for MVP
