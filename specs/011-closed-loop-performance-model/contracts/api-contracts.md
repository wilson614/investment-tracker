# API Contracts: Closed-Loop Performance Model & Transaction Type Redesign

**Feature**: 011-closed-loop-performance-model
**Date**: 2026-02-13
**Base URL**: `/api`

## 1) Currency Transaction Enum Contract (Breaking)

Currency transaction request/response payloads use redesigned enum names only.

- No compatibility aliases for deprecated enum names (FR-008).
- JSON enum continues to be string-based contract at API boundary.

### Affected DTO surfaces
- `CreateCurrencyTransactionRequest.transactionType`
- `UpdateCurrencyTransactionRequest.transactionType`
- `CurrencyTransactionDto.transactionType`

---

## 2) Create / Update Currency Transaction Validation Contract

### POST `/api/currencytransactions`
Create one currency transaction with strict ledger-currency/type validation.

### PUT `/api/currencytransactions/{id}`
Update one currency transaction with same strict validation.

### Validation failure response (400)
```json
{
  "error": "Business rule violation",
  "statusCode": 400,
  "timestamp": "2026-02-13T12:00:00Z"
}
```

> Notes:
> - Exact message body follows project-wide middleware error envelope.
> - Rule source must be backend policy matrix; frontend behavior cannot bypass.

---

## 3) Currency CSV Import Contract (New)

### POST `/api/currencytransactions/import`

Import CSV rows for a target ledger using atomic all-or-nothing semantics.

### Request
`multipart/form-data`

| Field | Type | Required | Description |
|---|---|---|---|
| `ledgerId` | string(Guid) | Yes | Target currency ledger |
| `file` | file(csv) | Yes | CSV content |

### Success response (200)
```json
{
  "status": "committed",
  "summary": {
    "totalRows": 25,
    "insertedRows": 25,
    "rejectedRows": 0
  },
  "errors": []
}
```

### Validation rejection response (422)
```json
{
  "status": "rejected",
  "summary": {
    "totalRows": 25,
    "insertedRows": 0,
    "rejectedRows": 25,
    "errorCount": 4
  },
  "errors": [
    {
      "rowNumber": 7,
      "fieldName": "transactionType",
      "invalidValue": "InitialBalance",
      "errorCode": "INVALID_TRANSACTION_TYPE_FOR_LEDGER",
      "message": "交易類型不符合此帳本規則",
      "correctionGuidance": "請改用 TransferInBalance 並確認該帳本允許此類型"
    },
    {
      "rowNumber": 9,
      "fieldName": "homeAmount",
      "invalidValue": "",
      "errorCode": "REQUIRED_FIELD_MISSING",
      "message": "此交易類型需要台幣成本",
      "correctionGuidance": "請填入整數台幣金額後重新匯入"
    }
  ]
}
```

### Contract guarantees
1. Any validation error => zero rows committed.
2. `errors` includes complete error set in single response.
3. Each error includes row number, field, invalid value, and correction guidance.

---

## 4) Annual Performance Contract Alignment

### Endpoint
- Existing annual performance endpoints remain, but behavior is corrected to new model semantics.

### Required behavior contract
1. Baseline formula at valuation points:
   - `V_t = stock market value + bound ledger balance`
2. Negative ledger balances must be included directly (no floor to zero).
3. Modified Dietz and TWR must consume same valuation baseline.
4. CF inclusion for MD/TWR uses explicit external categories only.
5. Non-TWD exchange flows:
   - `ExchangeBuy` included as positive external CF
   - `ExchangeSell` included as negative external CF

### Response fields (existing)
- `modifiedDietzPercentage`
- `timeWeightedReturnPercentage`
- valuation-related fields (`startValue*`, `endValue*`, `netContributions*`) must reflect closed-loop baseline semantics.

---

## 5) Frontend Text/Mapping Contract

### Required copy contract
- MD help text: `衡量比例的重壓 (Modified Dietz)`
- TWR help text: `衡量本金的重壓 (TWR)`

### Required mapping consistency
The same category semantics must be used across:
- transaction forms,
- currency detail labels,
- CSV import parser aliases,
- CSV export labels.
