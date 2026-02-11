# API Contract Changes: Ledger Exchange Cost Integration

**Feature**: 009-ledger-cost-nav
**Date**: 2026-02-11

## New Endpoint

### GET /api/currency-ledgers/{id}/exchange-rate-preview

Calculate the LIFO weighted-average exchange rate for a prospective stock purchase.

**Parameters**:

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `id` | path | Guid | Yes | Currency ledger ID |
| `amount` | query | decimal | Yes | Purchase amount in foreign currency |
| `date` | query | DateTime | Yes | Transaction date |

**Response 200**:

```json
{
  "rate": 31.0,
  "source": "lifo",
  "lifoRate": 31.0,
  "marketRate": 30.8,
  "lifoPortion": 150.0,
  "marketPortion": 0.0
}
```

**Response fields**:

| Field | Type | Description |
|-------|------|-------------|
| `rate` | decimal | The calculated exchange rate to use |
| `source` | string | Rate source: `"lifo"`, `"market"`, or `"blended"` |
| `lifoRate` | decimal? | LIFO weighted-average rate (null if no LIFO layers) |
| `marketRate` | decimal? | Market rate for the date (null if not fetched) |
| `lifoPortion` | decimal? | Amount covered by LIFO layers |
| `marketPortion` | decimal? | Amount requiring market rate (margin portion) |

**Response 400**: Invalid parameters
**Response 404**: Ledger not found or not owned by user

**Source mapping**:

| Scenario | `source` | `rate` |
|----------|----------|--------|
| Balance sufficient, LIFO layers exist | `"lifo"` | LIFO weighted average |
| Balance insufficient (partial margin) | `"blended"` | Weighted blend of LIFO + market |
| No LIFO layers, market rate available | `"market"` | Market rate |
| Balance = 0, full margin | `"market"` | Market rate |
| No LIFO layers AND no market rate | **422** error | — |

**Response 422**: Cannot determine exchange rate

```json
{
  "error": "ExchangeRateUnavailable",
  "message": "無法計算匯率。請先在帳本中建立換匯紀錄。"
}
```

---

## Modified Endpoint

### POST /api/stock-transactions

**Request body changes**:

| Field | Change | Type | Notes |
|-------|--------|------|-------|
| `exchangeRate` | **Removed** | ~~decimal?~~ | No longer accepted; system-calculated |
| `autoDeposit` | **Removed** | ~~bool~~ | Replaced by `balanceAction` |
| `balanceAction` | **Added** | int (enum) | `0` = None (default), `1` = Margin, `2` = TopUp |
| `topUpTransactionType` | **Added** | int? (enum) | Required when `balanceAction = 2`. CurrencyTransactionType value (1=ExchangeBuy, 3=Interest, 5=InitialBalance, 6=OtherIncome, 8=Deposit) |

**Example request (normal purchase, sufficient balance)**:

```json
{
  "portfolioId": "...",
  "transactionDate": "2026-02-11",
  "ticker": "AAPL",
  "transactionType": 1,
  "shares": 10,
  "pricePerShare": 185.50,
  "fees": 0,
  "market": 2,
  "currency": 2,
  "balanceAction": 0
}
```

**Example request (margin)**:

```json
{
  "portfolioId": "...",
  "transactionDate": "2026-02-11",
  "ticker": "AAPL",
  "transactionType": 1,
  "shares": 10,
  "pricePerShare": 185.50,
  "fees": 0,
  "market": 2,
  "currency": 2,
  "balanceAction": 1
}
```

**Example request (top up with ExchangeBuy)**:

```json
{
  "portfolioId": "...",
  "transactionDate": "2026-02-11",
  "ticker": "AAPL",
  "transactionType": 1,
  "shares": 10,
  "pricePerShare": 185.50,
  "fees": 0,
  "market": 2,
  "currency": 2,
  "balanceAction": 2,
  "topUpTransactionType": 1
}
```

**Response changes**:
- The response `exchangeRate` field now always reflects the system-calculated rate (LIFO, market, or blended).
- No changes to response structure otherwise.

**Error cases**:

| Condition | HTTP Status | Error |
|-----------|-------------|-------|
| Balance insufficient + `balanceAction = 0` (None) | 400 | `"帳本餘額不足，請選擇處理方式"` |
| `balanceAction = 2` (TopUp) without `topUpTransactionType` | 400 | `"補足餘額需指定交易類型"` |
| `topUpTransactionType` is an expense type | 400 | `"補足餘額的交易類型必須為入帳類型"` |
| Cannot determine exchange rate (no LIFO + no market) | 422 | `"無法計算匯率，請先在帳本中建立換匯紀錄"` |
| Market rate unavailable for top-up ExchangeBuy | 400 | `"無法取得市場匯率，請選擇其他交易類型或手動在帳本新增換匯紀錄"` |

---

## Unchanged Endpoints

All other currency and stock transaction endpoints remain unchanged. The exchange rate on `StockTransaction` is still returned in GET responses — only the source of the value changes (system-calculated instead of user-provided).
