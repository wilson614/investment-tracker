# API Contracts: Bank Account Enhancements

**Feature**: 006-bank-account-enhancements
**Date**: 2026-02-06

## Modified Endpoints

### Bank Accounts

#### GET /api/bank-accounts
Returns all bank accounts for current user.

**Response** (modified):
```json
[
  {
    "id": "uuid",
    "userId": "uuid",
    "bankName": "string",
    "totalAssets": 1000000.00,
    "interestRate": 1.5,
    "interestCap": 50000.00,
    "note": "string | null",
    "currency": "TWD",           // NEW
    "monthlyInterest": 625.00,
    "yearlyInterest": 7500.00,
    "isActive": true,
    "createdAt": "2026-02-06T00:00:00Z",
    "updatedAt": "2026-02-06T00:00:00Z"
  }
]
```

#### POST /api/bank-accounts
Create a bank account.

**Request** (modified):
```json
{
  "bankName": "string (required, max 100)",
  "totalAssets": 1000000.00,
  "interestRate": 1.5,
  "interestCap": 50000.00,
  "note": "string | null",
  "currency": "TWD"              // NEW (optional, default: "TWD")
}
```

**Validation**:
- `currency` must be one of: `TWD`, `USD`, `EUR`, `JPY`, `CNY`, `GBP`, `AUD`

#### PUT /api/bank-accounts/{id}
Update a bank account.

**Request** (modified):
```json
{
  "bankName": "string (required)",
  "totalAssets": 1000000.00,
  "interestRate": 1.5,
  "interestCap": 50000.00,
  "note": "string | null",
  "currency": "TWD"              // NEW
}
```

---

## New Endpoints

### Fund Allocations

#### GET /api/fund-allocations
Returns all fund allocations for current user.

**Response**:
```json
[
  {
    "id": "uuid",
    "userId": "uuid",
    "purpose": "EmergencyFund",
    "purposeDisplay": "緊急預備金",
    "amount": 500000.00,
    "createdAt": "2026-02-06T00:00:00Z",
    "updatedAt": "2026-02-06T00:00:00Z"
  }
]
```

#### POST /api/fund-allocations
Create a fund allocation.

**Request**:
```json
{
  "purpose": "EmergencyFund",
  "amount": 500000.00
}
```

**Validation**:
- `purpose` must be one of: `EmergencyFund`, `FamilyDeposit`, `General`, `Savings`
- `amount` must be >= 0
- Sum of all allocations must not exceed total bank assets in TWD
- One allocation per purpose per user (409 Conflict if duplicate)

**Response**: `201 Created` with created allocation

**Error Responses**:
- `400 Bad Request`: Invalid purpose or negative amount
- `409 Conflict`: Allocation for this purpose already exists
- `422 Unprocessable Entity`: Over-allocation (sum exceeds bank total)

#### PUT /api/fund-allocations/{id}
Update a fund allocation amount.

**Request**:
```json
{
  "amount": 600000.00
}
```

**Note**: Purpose cannot be changed. Delete and create new if needed.

**Validation**:
- `amount` must be >= 0
- Sum of all allocations (with updated amount) must not exceed total bank assets

**Response**: `200 OK` with updated allocation

#### DELETE /api/fund-allocations/{id}
Delete a fund allocation.

**Response**: `204 No Content`

---

## Modified Endpoints

### Total Assets

#### GET /api/assets/summary
Returns total assets summary with fund allocation breakdown.

**Response** (modified):
```json
{
  "investmentTotal": 5000000.00,
  "bankTotal": 2000000.00,
  "grandTotal": 7000000.00,
  "investmentPercentage": 71.43,
  "bankPercentage": 28.57,
  "totalMonthlyInterest": 1250.00,
  "totalYearlyInterest": 15000.00,
  "allocations": [                    // NEW
    {
      "purpose": "EmergencyFund",
      "purposeDisplay": "緊急預備金",
      "amount": 500000.00,
      "percentage": 25.00
    },
    {
      "purpose": "FamilyDeposit",
      "purposeDisplay": "家庭存款",
      "amount": 800000.00,
      "percentage": 40.00
    }
  ],
  "unallocatedAmount": 700000.00,     // NEW
  "hasOverAllocation": false           // NEW
}
```

**Calculation Notes**:
- `bankTotal`: Sum of all bank accounts converted to TWD using current exchange rates
- `unallocatedAmount`: `bankTotal` - sum of all allocation amounts
- `hasOverAllocation`: true if `unallocatedAmount` < 0
- `percentage`: (allocation amount / bankTotal) * 100

---

## Supported Currencies

| Code | Name | Symbol |
|------|------|--------|
| TWD | Taiwan Dollar | NT$ |
| USD | US Dollar | $ |
| EUR | Euro | € |
| JPY | Japanese Yen | ¥ |
| CNY | Chinese Yuan | ¥ |
| GBP | British Pound | £ |
| AUD | Australian Dollar | A$ |

---

## Error Response Format

All error responses follow existing format:

```json
{
  "type": "string",
  "title": "string",
  "status": 400,
  "detail": "string",
  "traceId": "string"
}
```

| Status | Use Case |
|--------|----------|
| 400 | Validation error (invalid currency, negative amount) |
| 403 | Access denied (not owner) |
| 404 | Resource not found |
| 409 | Conflict (duplicate allocation purpose) |
| 422 | Business rule violation (over-allocation) |
