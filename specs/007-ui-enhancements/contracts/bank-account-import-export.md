# API Contracts: Bank Account Export/Import

**Feature**: 007-ui-enhancements
**Date**: 2026-02-08

## Overview

This feature adds two new endpoints to the existing BankAccountsController for CSV export and import functionality.

---

## Endpoints

### GET /api/bank-accounts/export

Export all bank accounts for the current user as CSV.

**Request**:
- Method: GET
- Auth: Required (JWT)
- Query Parameters: None

**Response**:
- Content-Type: `text/csv; charset=utf-8`
- Content-Disposition: `attachment; filename="bank-accounts-{date}.csv"`
- Status: 200 OK

**Response Body** (CSV):
```csv
# InvestmentTracker Bank Accounts Export v1.0
BankName,TotalAssets,InterestRate,InterestCap,Currency,Note,IsActive
"台新銀行",1500000.00,0.0185,50000.00,TWD,"活存帳戶",true
"永豐銀行",500000.00,0.0200,30000.00,TWD,"定存",true
```

**Error Responses**:
| Status | Condition |
|--------|-----------|
| 401 Unauthorized | No valid JWT token |
| 500 Internal Server Error | Unexpected error |

---

### POST /api/bank-accounts/import

Import bank accounts from CSV data. Uses existing create/update endpoints internally.

**Note**: This is a batch preview endpoint. Actual creation/update uses existing individual endpoints.

**Request**:
- Method: POST
- Auth: Required (JWT)
- Content-Type: `application/json`

**Request Body**:
```json
{
  "accounts": [
    {
      "bankName": "台新銀行",
      "totalAssets": 1500000.00,
      "interestRate": 0.0185,
      "interestCap": 50000.00,
      "currency": "TWD",
      "note": "活存帳戶",
      "isActive": true
    }
  ],
  "mode": "preview" | "execute"
}
```

**Response (mode: preview)**:
- Status: 200 OK
- Content-Type: `application/json`

```json
{
  "valid": true,
  "summary": {
    "toCreate": 2,
    "toUpdate": 1,
    "errors": 0
  },
  "items": [
    {
      "bankName": "台新銀行",
      "action": "update",
      "existingId": "uuid-here",
      "changes": ["totalAssets: 1400000 → 1500000"]
    },
    {
      "bankName": "新光銀行",
      "action": "create",
      "existingId": null,
      "changes": null
    }
  ],
  "errors": []
}
```

**Response (mode: execute)**:
- Status: 200 OK

```json
{
  "success": true,
  "created": 2,
  "updated": 1,
  "failed": 0,
  "results": [
    { "bankName": "台新銀行", "status": "updated", "id": "uuid" },
    { "bankName": "新光銀行", "status": "created", "id": "uuid" }
  ]
}
```

**Error Responses**:
| Status | Condition | Response Body |
|--------|-----------|---------------|
| 400 Bad Request | Validation errors | `{ "valid": false, "errors": [...] }` |
| 401 Unauthorized | No valid JWT token | Standard error |
| 500 Internal Server Error | Unexpected error | Standard error |

---

## DTOs

### BankAccountImportItemDto

```csharp
public record BankAccountImportItemDto(
    string BankName,
    decimal TotalAssets,
    decimal InterestRate,
    decimal? InterestCap,
    string Currency,
    string? Note,
    bool IsActive
);
```

### BankAccountImportRequestDto

```csharp
public record BankAccountImportRequestDto(
    List<BankAccountImportItemDto> Accounts,
    string Mode // "preview" or "execute"
);
```

### ImportPreviewResultDto

```csharp
public record ImportPreviewResultDto(
    bool Valid,
    ImportSummaryDto Summary,
    List<ImportItemResultDto> Items,
    List<string> Errors
);

public record ImportSummaryDto(
    int ToCreate,
    int ToUpdate,
    int Errors
);

public record ImportItemResultDto(
    string BankName,
    string Action, // "create" | "update" | "skip"
    Guid? ExistingId,
    List<string>? Changes
);
```

---

## Frontend API Integration

### useBankAccountExport Hook

```typescript
export function useBankAccountExport() {
  const exportToCSV = async () => {
    const response = await fetch('/api/bank-accounts/export', {
      headers: { Authorization: `Bearer ${token}` }
    });
    const blob = await response.blob();
    // Trigger download
  };
  return { exportToCSV };
}
```

### useBankAccountImport Hook

```typescript
export function useBankAccountImport() {
  const preview = async (accounts: BankAccountImportItem[]) => {
    return api.post('/api/bank-accounts/import', { accounts, mode: 'preview' });
  };

  const execute = async (accounts: BankAccountImportItem[]) => {
    return api.post('/api/bank-accounts/import', { accounts, mode: 'execute' });
  };

  return { preview, execute };
}
```
