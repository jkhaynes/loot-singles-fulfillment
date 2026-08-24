# Data Model: Basic Production Logging

No persisted entity, schema, or migration is added or changed by this feature. `ImportAttempt`, `ImportOrderResult`, and `FailureType` (all existing, from feature 001) are read-only inputs to the log statements below; none of their fields, relationships, or constraints change.

This feature's only "shapes" are the structured log entries it emits. Each is a single `ILogger<PackingSlipImportService>` call with named, individually-queryable arguments (per FR-007) — not a message built by string interpolation.

## Log entry: import attempt completion

Emitted exactly once per import attempt (research.md §4), after `ImportAttempt.Id` is guaranteed assigned (research.md §3).

| Field | Source | Always present? |
|-------|--------|------------------|
| `ImportId` | `attempt.Id` | Yes |
| `OrdersDetected` | count of `parsed.OrderBlocks` (0 for an unreadable/unparseable file) | Yes |
| `OrdersSucceeded` | count of `ImportOrderResult` with `Outcome == Succeeded` | Yes |
| `OrdersFailed` | count of `ImportOrderResult` with `Outcome == Rejected` | Yes |
| `AttemptFailureType` | `attempt.AttemptFailureCode` (`UnreadablePdf`, `SummaryMismatch`, or absent) | Only when set |
| Failure-type breakdown | count of failed orders grouped by their own `FailureType`, one named argument per type present (e.g., `DuplicateOrderCount`, `MissingProductNameCount`) | Only for the types actually present in that attempt |
| Failure-type order identifiers | for each failure type present, the affected orders' `SourceOrderIdentifier` values (comma-joined), one named argument per type (e.g., `DuplicateOrderIds`, `MissingProductNameIds`); no cap on how many are listed. `MissingOrderIdentifier`'s own group uses the literal placeholder `(missing)` instead of an empty identifier. | Only for the types actually present in that attempt |

The free-text message MUST also label each failure-type group by name (e.g., `DuplicateOrder: 13 [F0000010-ABC010-00010,...]`), not just embed the bare count/identifier values — the default console formatter only ever prints the formatted message text, never the named structured arguments separately, so an unlabeled value pair (e.g. `13 F0000010-...`) is unreadable on its own and fails SC-005's "console output alone" requirement.

**Level**: `Information` when `AttemptFailureType` is absent and `OrdersFailed == 0`; otherwise `Warning`.

## Log entry: unexpected order persistence failure

Emitted at most once per affected order, immediately when caught (research.md §5), in addition to being reflected in that attempt's completion entry above.

| Field | Source | Always present? |
|-------|--------|------------------|
| `ImportId` | `attempt.Id` | Yes |
| `OrderId` | `block.OrderIdentifier` (the source TCGplayer order identifier; the row's own database id was never assigned since its save failed) | Yes |
| `FailureType` | `FailureType.PersistenceFailure` | Yes |

**Level**: `Error`.

## Explicitly excluded from every log entry

Per FR-008 and User Story 3: customer name, customer address, any raw packing-slip text (product descriptions, condition strings, etc. as extracted from the source PDF), passwords, PINs, authentication tokens, and connection strings. Only the identifiers, counts, statuses, and failure-type values listed above are logged.
