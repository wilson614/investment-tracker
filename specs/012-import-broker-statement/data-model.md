# Data Model: Unified Broker Statement Import

## 1) ImportSession

Represents one user import attempt from upload to execution result.

### Fields
- `sessionId` (string, UUID) - unique session identity
- `portfolioId` (string, UUID) - target portfolio
- `userId` (string, UUID) - session owner
- `sourceFileName` (string, max 255)
- `detectedFormat` (enum: `legacy_csv`, `broker_statement`, `unknown`)
- `selectedFormat` (enum: `legacy_csv`, `broker_statement`)
- `status` (enum: `uploaded`, `previewed`, `ready_to_execute`, `executed`, `partially_executed`, `rejected`)
- `totalRows` (int, >= 0)
- `resolvableRows` (int, >= 0)
- `unresolvedRows` (int, >= 0)
- `createdAt` (datetime UTC)
- `updatedAt` (datetime UTC)

### Validation Rules
- `selectedFormat` must be present before execution.
- `status=ready_to_execute` requires preview completion and no unresolved mandatory user decisions.

### State Transitions
`uploaded -> previewed -> ready_to_execute -> (executed | partially_executed | rejected)`

---

## 2) ImportRow

Normalized candidate transaction from one CSV row.

### Fields
- `sessionId` (string, UUID)
- `rowNumber` (int, >= 1; original file order)
- `rawSecurityName` (string, nullable)
- `resolvedTicker` (string, nullable)
- `tradeDate` (date)
- `tradeSide` (enum: `buy`, `sell`, `ambiguous`)
- `quantity` (decimal, > 0)
- `unitPrice` (decimal, >= 0)
- `grossAmount` (decimal, nullable)
- `fees` (decimal, >= 0, default 0)
- `taxes` (decimal, >= 0, default 0)
- `netSettlement` (decimal, nullable)
- `currency` (string, ISO-like, normalized)
- `validationStatus` (enum: `valid`, `invalid`, `requires_user_action`)
- `executionStatus` (enum: `pending`, `created`, `failed`, `skipped`)
- `failureCode` (string, nullable)
- `failureMessage` (string, nullable)

### Validation Rules
- Buy/sell side derivation uses canonical net-settlement sign rule.
- `ambiguous` side requires user confirmation before execution.
- Missing/ambiguous ticker requires user input or explicit exclusion before execution.

---

## 3) BalanceHandlingDecision

User decision for rows that would cause insufficient balance.

### Fields
- `sessionId` (string, UUID)
- `rowNumber` (int, >= 1)
- `requiredAmount` (decimal, > 0)
- `availableBalance` (decimal)
- `shortfall` (decimal, > 0)
- `action` (enum: `none`, `margin`, `top_up`)
- `topUpTransactionType` (enum, nullable; required when `action=top_up`)
- `decisionScope` (enum: `global_default`, `row_override`)

### Validation Rules
- `action` must be `margin` or `top_up` for shortfall rows.
- `topUpTransactionType` required only when `action=top_up`.
- Allowed top-up transaction types must match manual create policy.

---

## 4) TwSecurityMapping

Persistent local mapping used for broker statement security resolution.

### Fields
- `ticker` (string, PK)
- `securityName` (string, indexed)
- `isin` (string, nullable)
- `market` (string, nullable)
- `currency` (string, nullable)
- `source` (enum: `twse_isin`)
- `lastSyncedAt` (datetime UTC)
- `createdAt` (datetime UTC)
- `updatedAt` (datetime UTC)

### Validation Rules
- `ticker` normalized uppercase and unique.
- `securityName` normalized for matching pipeline.

### Relationship Notes
- One `securityName` may map to multiple tickers; unresolved/non-unique match requires user action.

---

## 5) ImportExecutionResult

Execution summary and diagnostics returned to UI.

### Fields
- `status` (enum: `committed`, `partially_committed`, `rejected`)
- `summary.totalRows` (int)
- `summary.insertedRows` (int)
- `summary.failedRows` (int)
- `summary.errorCount` (int)
- `errors[]`:
  - `rowNumber` (int)
  - `fieldName` (string)
  - `invalidValue` (string, nullable)
  - `errorCode` (string)
  - `message` (string)
  - `correctionGuidance` (string)

### Validation Rules
- Every failed/unresolved row must have at least one diagnostic.
- Result ordering of row diagnostics should follow `rowNumber` ascending.

---

## Scale Assumptions
- Typical import size: <= 500 rows per file (primary target).
- Maximum safeguarded size to be enforced by validation policy (server-side file/data row limits).
