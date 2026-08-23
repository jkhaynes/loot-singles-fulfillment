# Feature Specification: Basic Production Logging

**Feature Branch**: `006-production-logging`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "Add basic production logging to Loot Singles Fulfillment. Use ASP.NET Core ILogger<T> with structured logging. Log important application events and failures, especially around order imports and order workflow. Keep logging simple, maintainable, and free. Use console/stdout logging so Azure Container Apps can expose logs through its log stream. Do not add Serilog, Application Insights, Log Analytics, Seq, Datadog, or any other paid/external logging platform. Use appropriate Information, Warning, and Error levels. Include useful safe identifiers such as ImportId, order ID, counts, statuses, and failure types. Never log customer names, addresses, raw packing-slip contents, passwords, tokens, connection strings, secrets, or other unnecessary PII. Do not create a custom logging abstraction such as ILoggingService; use ILogger<T> directly. Avoid excessive logging and do not log every parsing step or method call. Do not change existing business behavior."

## Clarifications

### Session 2026-08-23

- Q: Should this feature also add `ILogger` logging for authentication/employee-management events (login failure, account lockout, account created/deactivated/PIN reset), or is it scoped strictly to order import and order-listing/dashboard reads? → A: Import/order only. Auth/employee events remain covered by the existing `EmployeeAuditEvent` database audit trail; console logging for them is deferred to a later feature if a real operational need arises.
- Q: Beyond order import, does "order workflow" logging need to cover the order-listing and Ready-dashboard read paths, or is import the only order-related surface worth logging today? → A: Import only. Order-listing/dashboard reads are simple queries with no business-meaningful failure mode of their own (an unexpected error already surfaces as an unhandled 500), so this feature does not add logging to those read paths.
- Q: Should the Warning-level import-completion log (when some orders in a batch failed) include a breakdown of which failure types occurred, or just the overall succeeded/failed counts? → A: Include a failure-type breakdown (e.g., counts per `FailureType` such as `DuplicateOrder` or `MissingProductName`) in the same single completion log entry, so an operator can tell why a batch had failures from console output alone, without needing database access.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Diagnose a Failed or Problematic Import from Production Logs (Priority: P1)

As an on-call operator responding to a report that an order import went wrong, I need the running application's console log output to show what happened during that import attempt — including how many orders it found, how many succeeded or failed, and why — so I can understand and explain the problem without needing direct database access.

**Why this priority**: Order import is the entry point for all fulfillment data. When it misbehaves, the business is currently blind unless someone inspects the database directly. This is the single highest-value use of production logging.

**Independent Test**: Trigger a packing-slip import that is unreadable, that contains a rejected order, and one that succeeds; confirm each produces a distinct, readable log entry in the console output identifying the attempt and its outcome, without inspecting the database.

**Acceptance Scenarios**:

1. **Given** a packing-slip import completes with a mix of succeeded and rejected orders, **When** the import attempt finishes, **Then** the console log contains one entry identifying the import attempt and reporting the number of orders detected, succeeded, and failed.
2. **Given** a supplied file cannot be read as a TCGplayer packing slip, **When** the import is attempted, **Then** the console log contains a warning entry identifying the import attempt and the reason it could not be read.
3. **Given** an order fails to save because of an unexpected persistence problem (not an ordinary duplicate-order rejection), **When** that failure occurs, **Then** the console log contains an error entry identifying the import attempt, the affected order, and the failure type.

---

### User Story 2 - Confirm the System Is Healthy from Steady-State Logs (Priority: P2)

As an operator glancing at the live log stream, I need normal, successful activity to also produce a concise log entry, so I can tell the system is actively and correctly processing work rather than silently doing nothing.

**Why this priority**: Logs that only ever appear on failure make it impossible to distinguish "nothing is happening" from "everything is fine." This is valuable but secondary to diagnosing active problems.

**Independent Test**: Perform a fully successful import and confirm exactly one informational log entry appears summarizing the outcome, with no other unrelated log noise introduced by this feature.

**Acceptance Scenarios**:

1. **Given** a packing-slip import completes with every order succeeding, **When** the import attempt finishes, **Then** the console log contains one informational entry reporting the successful outcome and counts.

---

### User Story 3 - Trust That Production Logs Never Leak Sensitive Data (Priority: P1)

As the business owner, I need assurance that anything written to the application's production logs is safe for on-call staff to view, so that adding operational visibility never becomes a privacy or security incident.

**Why this priority**: Console logs in Azure Container Apps are more broadly accessible than the application database. A single leaked customer name, packing-slip line, or credential in a log line would be a real incident, not a cosmetic issue. This constraint governs every other log statement this feature adds.

**Independent Test**: Perform imports covering successful, rejected, and unreadable cases and inspect every resulting log entry; confirm none contains a customer name, address, packing-slip content, password, PIN, token, or connection string.

**Acceptance Scenarios**:

1. **Given** any log entry produced by this feature, **When** it is inspected, **Then** it contains only safe identifiers (such as an import attempt identifier, order identifier, counts, statuses, and failure types) and no customer-identifying or secret content.

---

### Edge Cases

- A large batch import (hundreds of orders) MUST NOT produce one log entry per order or per product line; log volume stays bounded to attempt-level entries plus the rare individual-failure entry described in FR-006.
- A client cancels an import mid-stream (for example, by navigating away): this is normal, expected client behavior, not a failure, and MUST NOT produce a warning or error entry.
- An import attempt that finds a readable file but zero recognizable order pages is treated the same as an unreadable file for logging purposes (a warning, not an error).
- Multiple duplicate-order rejections within the same batch are ordinary, expected outcomes; they are reflected only as a count within the single attempt-level completion log entry's failure-type breakdown (FR-004) and MUST NOT each produce their own log entry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST use ASP.NET Core's built-in `ILogger<T>` (constructor-injected per class) as the only logging mechanism. No new logging package, third-party or paid logging platform, or custom logging abstraction (such as an `ILoggingService`) MAY be introduced.
- **FR-002**: All log output MUST be delivered through console/stdout logging only, so it is visible through the hosting platform's log stream, with no file, database, or external log sink added.
- **FR-003**: When an order-import attempt finishes with every order succeeding, the system MUST emit exactly one Information-level log entry reporting the import attempt's identifier and the counts of orders detected and succeeded.
- **FR-004**: When an order-import attempt finishes with at least one order failing (but the attempt itself was able to run), the system MUST emit exactly one Warning-level log entry reporting the import attempt's identifier, the counts of orders detected/succeeded/failed, and a breakdown of how many failed orders fall under each failure type present in that attempt (for example, a count of `DuplicateOrder` and a separate count of `MissingProductName`), so the reason for the failures is visible without querying the database.
- **FR-005**: When a supplied file cannot be parsed as a TCGplayer packing slip (unreadable file, or a readable file with no recognizable order pages), the system MUST emit a Warning-level log entry identifying the import attempt and the reason, without including any content extracted from the file.
- **FR-006**: When an order fails to save during import because of an unexpected persistence problem — as distinct from an ordinary duplicate-order rejection, which is a normal, already-logged-in-the-summary business outcome — the system MUST emit an Error-level log entry identifying the import attempt, the affected order's identifier, and the failure type.
- **FR-007**: Every log entry this feature adds MUST use structured, named values (for example, an import attempt identifier, order identifier, counts, status, or failure type) passed as distinct logging arguments rather than only interpolated into a free-text message, so the values remain individually identifiable in log output.
- **FR-008**: Log entries added by this feature MUST NOT include a customer's name or address, any raw content extracted from a packing slip, or any password, PIN, authentication token, connection string, or other secret.
- **FR-009**: The system MUST NOT emit a log entry for each individual parsing step, each product line, or each internal method call; only attempt-level and individual-failure-level outcomes (per FR-003 through FR-006) produce log entries.
- **FR-010**: Adding this logging MUST NOT change any existing business behavior, API response, or persisted data; it is an additive, observability-only change.
- **FR-011**: The default logging configuration MUST continue to allow Information, Warning, and Error entries to reach console output in the application's normal running configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For any completed import attempt, an operator can determine from console log output alone — without querying the database — how many orders were detected, how many succeeded or failed, and (when applicable) which failure type(s) occurred; for an order that failed because of an unexpected persistence problem, the operator can additionally identify that specific order.
- **SC-002**: A review of log entries produced across successful, partially-failed, and unreadable import scenarios finds zero instances of customer name, address, packing-slip content, or credential/secret data.
- **SC-003**: Importing a 200-order batch produces a small, bounded number of log entries (not one per order), keeping log output readable during high-volume activity.
- **SC-004**: All existing automated tests continue to pass unchanged after this feature is implemented, confirming no business behavior was altered.
- **SC-005**: An operator with access only to the running application's console log output (no debugger, no direct database access) can identify why a specific failed import did not fully succeed.

## Assumptions

- Azure Container Apps' log stream reads directly from the container's console/stdout; no additional export, shipping, or aggregation configuration is required or in scope for this feature.
- Authentication and employee-management events (login, lockout, account creation, PIN reset, and similar) already have a persisted, queryable audit trail (`EmployeeAuditEvent`) from a prior feature. Per clarification, adding console/stdout logging for those events is confirmed out of scope for this feature and may be considered separately if a real operational need arises.
- Per clarification, "order workflow" in this feature's scope means the order-import pipeline only. Order-listing and Ready-dashboard reads have no business-meaningful failure mode of their own today and are confirmed out of scope; the order-claiming and picking workflows described in the product PRD are not yet implemented and are also out of scope.
- Log retention, search, and alerting within Azure Container Apps' log stream are operational concerns outside this feature; this feature is only responsible for emitting useful, safe entries to stdout.
- Existing default logging configuration (Information threshold, with a higher threshold for framework-internal categories) is reused as-is; no new configuration section or environment-specific override is required unless clarification reveals otherwise.
