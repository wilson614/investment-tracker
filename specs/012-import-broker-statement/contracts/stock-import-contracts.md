# API Contracts: Unified Stock Import

## 1) Preview Stock Import

**Endpoint**: `POST /api/stocktransactions/import/preview`

### Request Body
```json
{
  "portfolioId": "uuid",
  "csvContent": "string",
  "selectedFormat": "legacy_csv|broker_statement"
}
```

### Response 200
```json
{
  "sessionId": "uuid",
  "detectedFormat": "legacy_csv|broker_statement|unknown",
  "selectedFormat": "legacy_csv|broker_statement",
  "summary": {
    "totalRows": 120,
    "validRows": 112,
    "requiresActionRows": 6,
    "invalidRows": 2
  },
  "rows": [
    {
      "rowNumber": 1,
      "tradeDate": "2026-01-22",
      "rawSecurityName": "台積電",
      "ticker": "2330",
      "tradeSide": "buy",
      "quantity": 1000,
      "unitPrice": 625,
      "fees": 1425,
      "taxes": 0,
      "netSettlement": -626425,
      "currency": "TWD",
      "status": "valid|requires_user_action|invalid",
      "actionsRequired": ["confirm_trade_side", "input_ticker", "select_balance_action"]
    }
  ],
  "errors": [
    {
      "rowNumber": 10,
      "fieldName": "SecurityName",
      "invalidValue": "XXX",
      "errorCode": "SYMBOL_UNRESOLVED",
      "message": "Security identity cannot be resolved uniquely",
      "correctionGuidance": "Enter ticker manually or exclude this row"
    }
  ]
}
```

### Validation Notes
- `portfolioId` must belong to current user.
- `selectedFormat` can override detection result.
- Preview must include unresolved rows in original file order.

---

## 2) Execute Stock Import

**Endpoint**: `POST /api/stocktransactions/import/execute`

### Request Body
```json
{
  "sessionId": "uuid",
  "portfolioId": "uuid",
  "rows": [
    {
      "rowNumber": 1,
      "ticker": "2330",
      "confirmedTradeSide": "buy|sell",
      "exclude": false,
      "balanceAction": "None|Margin|TopUp",
      "topUpTransactionType": "Deposit|InitialBalance|Interest|OtherIncome"
    }
  ],
  "defaultBalanceAction": {
    "action": "Margin|TopUp",
    "topUpTransactionType": "Deposit|InitialBalance|Interest|OtherIncome"
  }
}
```

### Response 200
```json
{
  "status": "committed|partially_committed|rejected",
  "summary": {
    "totalRows": 120,
    "insertedRows": 110,
    "failedRows": 10,
    "errorCount": 10
  },
  "results": [
    {
      "rowNumber": 1,
      "success": true,
      "transactionId": "uuid",
      "message": "Created"
    },
    {
      "rowNumber": 10,
      "success": false,
      "errorCode": "BALANCE_ACTION_REQUIRED",
      "message": "Insufficient balance decision is required"
    }
  ],
  "errors": [
    {
      "rowNumber": 10,
      "fieldName": "BalanceAction",
      "invalidValue": null,
      "errorCode": "BALANCE_ACTION_REQUIRED",
      "message": "Insufficient balance decision is required",
      "correctionGuidance": "Select Margin or Top-up before execution"
    }
  ]
}
```

### Validation Notes
- For every shortfall buy row, resolved action must be `Margin` or `TopUp`.
- `topUpTransactionType` is mandatory only when action is `TopUp`.
- Unresolved ticker rows must be blocked until user provides ticker or excludes row.
- Sync failure must not block execution of already resolvable rows.

---

## 3) On-demand TW Mapping Synchronization

**Endpoint**: `POST /api/market-data/twse/symbol-mappings/sync-on-demand`

### Request Body
```json
{
  "securityNames": ["台積電", "聯發科"]
}
```

### Response 200
```json
{
  "requested": 2,
  "resolved": 1,
  "unresolved": 1,
  "mappings": [
    {
      "securityName": "台積電",
      "ticker": "2330",
      "isin": "TW0002330008",
      "market": "TWSE"
    }
  ],
  "errors": [
    {
      "securityName": "未知公司",
      "errorCode": "NOT_FOUND",
      "message": "No mapping found after synchronization"
    }
  ]
}
```

### Error Semantics
- Upstream unavailable should return actionable error information while allowing import flow to continue with unresolved rows.

---

## 4) Shared Diagnostic Object (canonical)

```json
{
  "rowNumber": 0,
  "fieldName": "string",
  "invalidValue": "string|null",
  "errorCode": "string",
  "message": "string",
  "correctionGuidance": "string"
}
```

Used by both preview and execute responses for deterministic UI rendering.
