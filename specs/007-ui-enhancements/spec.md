# Feature Specification: UI Enhancements Batch

**Feature Branch**: `007-ui-enhancements`
**Created**: 2026-02-08
**Status**: Draft
**Input**: User description: "功能包含三項：(1) 股票交易視窗閃屏修復與 UI 優化 (2) 帳本頁面改用下拉切換 (3) 銀行帳戶匯出匯入功能"

---

## Clarifications

### Session 2026-02-08

- Q: What file format should be used for bank account export/import? → A: CSV format, following the existing pattern used for investment transactions and ledger export/import features
- Q: How to optimize the trading dialog UI that is too long? → A: Remove the "Linked Ledger" section entirely (since this is a transaction record, not a trading feature) and reduce the size of the notes/remarks field

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fix Stock Trading Dialog Flicker (Priority: P1)

When a user opens the stock trading dialog, the "Linked Ledger" section at the bottom flickers and causes visual instability. The dialog content is also too long, requiring scrolling and degrading the user experience.

**Why this priority**: This is a bug fix that affects core trading functionality. Users experience visual distraction during critical financial operations, potentially leading to input errors.

**Independent Test**: Can be tested by opening the stock trading dialog and observing stable rendering without any flickering or layout shifts.

**Acceptance Scenarios**:

1. **Given** a user is on the portfolio page, **When** they click "Add Transaction" to open the stock trading dialog, **Then** the dialog renders completely without any flickering or layout shifts
2. **Given** the stock trading dialog is open, **When** the user views the dialog, **Then** the "Linked Ledger" section is not displayed (removed)
3. **Given** the stock trading dialog is open on a standard desktop screen (1080p), **When** all fields are visible, **Then** the dialog content fits within the viewport without requiring scrolling for primary fields

---

### User Story 2 - Ledger Dropdown Navigation (Priority: P2)

Currently, the ledger page has an overview page listing all ledgers. Users want a streamlined navigation experience similar to the portfolio page, where they can switch between ledgers using a dropdown selector in the top-left corner instead of navigating through an overview page.

**Why this priority**: This is a UX improvement that streamlines navigation and reduces clicks. It aligns the ledger experience with the portfolio experience for consistency.

**Independent Test**: Can be tested by navigating to the ledger page and using the dropdown to switch between different currency ledgers without page reloads.

**Acceptance Scenarios**:

1. **Given** a user navigates to the ledger section, **When** the page loads, **Then** the most recently viewed ledger is displayed with a dropdown selector in the top-left corner
2. **Given** a user is viewing a ledger, **When** they click the dropdown selector, **Then** they see a list of all available ledgers grouped or labeled by currency
3. **Given** a user has the ledger dropdown open, **When** they select a different ledger, **Then** the view switches to the selected ledger without a full page navigation
4. **Given** a user selects a ledger, **When** they navigate away and return to the ledger section, **Then** their last selected ledger is remembered

---

### User Story 3 - Bank Account Export/Import (Priority: P3)

Users want to export their bank account data for backup or external analysis, and import bank account data to quickly set up or restore their accounts.

**Why this priority**: This is a new feature that adds data portability. While valuable, it doesn't affect core functionality and can be implemented after the UX improvements.

**Independent Test**: Can be tested by exporting bank accounts to a file, modifying the file, and importing it back to verify data integrity.

**Acceptance Scenarios**:

1. **Given** a user is on the bank accounts page with existing accounts, **When** they click the "Export" button, **Then** a file containing all their bank account data is downloaded
2. **Given** a user has an export file, **When** they click "Import" and select the file, **Then** the system previews the accounts to be imported before confirmation
3. **Given** a user is importing accounts, **When** some accounts have the same name as existing accounts, **Then** the system clearly indicates which accounts will be updated vs. created new
4. **Given** a user confirms the import, **When** the import completes, **Then** all imported accounts appear in the bank accounts list with correct data
5. **Given** a user attempts to import an invalid or corrupted file, **When** the import fails, **Then** the system displays a clear error message and no data is modified

---

### Edge Cases

- What happens when a user has no ledgers? The dropdown should show an empty state with guidance to create a portfolio first (ledgers are auto-created with portfolios).
- What happens when the export file format changes between versions? The import should validate the file version and show a compatibility warning if needed.
- What happens when a user imports a file with accounts in unsupported currencies? The system should reject those specific accounts and report which ones failed.
- How does the trading dialog behave on mobile/small screens? The dialog should be scrollable with primary action buttons always visible.

---

## Requirements *(mandatory)*

### Functional Requirements

**Stock Trading Dialog (Feature 1)**:
- **FR-001**: System MUST render the stock trading dialog without visual flickering when opened
- **FR-002**: System MUST NOT display the "Linked Ledger" section in the trading dialog (this is a transaction record feature, not a live trading feature)
- **FR-003**: System MUST display all primary input fields within the initial viewport on standard desktop screens (1920x1080)
- **FR-004**: System MUST keep action buttons (Save/Cancel) visible without scrolling on desktop screens
- **FR-005**: System MUST reduce the notes/remarks field to a compact single-line or minimal-height input

**Ledger Navigation (Feature 2)**:
- **FR-006**: System MUST provide a dropdown selector for switching between ledgers, positioned in the top-left corner of the ledger view
- **FR-007**: System MUST switch ledger views without triggering a full page reload
- **FR-008**: System MUST remember the user's last selected ledger across sessions
- **FR-009**: System MUST display the current ledger's currency label in the dropdown trigger button
- **FR-010**: System MUST remove the separate ledger overview page (direct navigation to ledger detail with dropdown)

**Bank Account Export/Import (Feature 3)**:
- **FR-011**: System MUST allow users to export all bank accounts to a downloadable CSV file
- **FR-012**: System MUST support importing bank accounts from a previously exported CSV file
- **FR-013**: System MUST preview import data before applying changes
- **FR-014**: System MUST handle duplicate detection based on bank account name
- **FR-015**: System MUST validate import file format and report errors clearly
- **FR-016**: Export file MUST include all bank account fields: name, total assets, interest rate, interest cap, currency, note, active status

### Key Entities

- **Ledger**: Represents a currency-based transaction ledger linked to a portfolio. Key attributes: currency, portfolio association, transactions list.
- **Bank Account**: Represents a user's bank account. Key attributes: bank name, total assets, interest rate (4 decimal places), interest cap, currency (default TWD), note, active status.
- **Export File**: A CSV file containing bank account data for backup/restore purposes. Must follow the existing export/import pattern used for investment transactions and ledger features. Includes version header for forward compatibility.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Stock trading dialog opens and renders completely within 500ms without any visible flickering or layout shifts
- **SC-002**: Users can switch between ledgers in under 2 clicks (1 click to open dropdown, 1 click to select)
- **SC-003**: Ledger switching occurs instantly without page reload (perceived latency under 200ms)
- **SC-004**: 100% of bank account data survives a complete export-import round trip with data integrity preserved
- **SC-005**: Users can complete a bank account export in under 3 clicks
- **SC-006**: Users can complete a bank account import (including preview confirmation) in under 5 clicks
- **SC-007**: Import validation catches and reports 100% of format errors before any data modification occurs
