# Data Model: TCGplayer Packing Slip Order Import

Derived from spec.md's Key Entities section and the Functional Requirements that constrain them. Field names are illustrative, not a literal schema mandate — types and exact naming are an implementation detail, but the constraints listed (required/optional, uniqueness, atomicity boundary) are requirements, not suggestions.

## Order

Represents one TCGplayer order recovered from a packing slip (FR-001).

| Field | Type | Constraint |
|---|---|---|
| Id | identity | Primary key |
| TcgplayerOrderId | string | **Required. Unique** (database-enforced, FR-008) |
| Status | enum | Set to `Ready` on creation (spec Assumptions) |
| ImportedAt | timestamp | Set on creation |

- Contains one or more `OrderLine`s (FR-001, FR-004).
- **Does not** contain any customer shipping field (name, address, contact detail) — FR-009. No such column exists on this entity; this is not a masking/redaction rule, the data is never mapped in.
- Created together with all of its `OrderLine`s in a single atomic operation (FR-016) — an `Order` row must never exist without at least one `OrderLine` (FR-007).

## OrderLine

Represents one product/card line item within an `Order`, as described by the packing slip (FR-002, FR-017, FR-018).

| Field | Type | Constraint |
|---|---|---|
| Id | identity | Primary key |
| OrderId | FK → Order | Required. Belongs to exactly one Order (FR-004) |
| RawDescription | string | **Required.** Verbatim source text, unmodified (FR-017) |
| ProductLine | string | Required (game, e.g. "Pokemon", "Magic") — FR-002 |
| ProductName | string | **Required** — rejection if missing/unreadable (FR-005) |
| Set | string | **Required** — rejection if missing/unreadable (FR-005) |
| CollectorNumber | string | **Required** — rejection if missing/unreadable (FR-005) |
| Rarity | string, nullable | **Optional** — absence does not cause rejection (FR-005) |
| Condition | string | **Required**, parsed separately from Variant even when the source combines them (FR-005, FR-018) |
| Variant | string, nullable | **Optional field, but mandatory to preserve when present** in the source, from any location in the description (FR-018) |
| Quantity | int | **Required.** Must be a positive whole number; preserved exactly, no rounding/capping (FR-003, FR-005) |

- No price/total-price field — intentionally not extracted (spec Assumptions; not needed for picking).
- No catalog/image enrichment fields — this feature never populates them (FR-011).

## ImportAttempt

Represents one execution of the import process against one supplied packing slip file (spec Key Entities; FR-012–FR-015, FR-019).

| Field | Type | Constraint |
|---|---|---|
| Id | identity | Primary key |
| StartedAt | timestamp | |
| CompletedAt | timestamp, nullable | Null while still processing |
| AttemptFailureCode | enum, nullable | One of `UnreadablePdf`, `SummaryMismatch` (FR-006, FR-013, FR-015); null if the file itself was readable and no summary discrepancy occurred |
| AttemptFailureMessage | string, nullable | Human-readable detail when `AttemptFailureCode` is set (FR-006) |

- Contains one or more `ImportOrderResult`s, one per order the attempt evaluated (empty when `AttemptFailureCode = UnreadablePdf`, since no orders could be evaluated at all — FR-015).
- Progress counters (orders detected/processed so far, running success/failure counts — FR-014) are **transient, in-flight state** surfaced via `IAsyncEnumerable<ImportProgressUpdate>` (see contracts/import-service.md and research.md §4) while the attempt is running; they are not separately persisted columns, since the final counts are always derivable from the completed set of `ImportOrderResult`s.
- **No reference to the source file** is stored on this entity or anywhere else — FR-019. Not even a filename that could indirectly leak customer identity is required by any FR.

## ImportOrderResult

Represents the outcome for one order within an `ImportAttempt` (spec Key Entities; FR-005, FR-006, FR-012).

| Field | Type | Constraint |
|---|---|---|
| Id | identity | Primary key |
| ImportAttemptId | FK → ImportAttempt | Required. Belongs to exactly one ImportAttempt |
| SourceOrderIdentifier | string, nullable | Null only when the identifier itself could not be read (`MissingOrderIdentifier`) |
| Outcome | enum | `Succeeded` \| `Rejected` |
| FailureCode | enum, nullable | One of `MissingOrderIdentifier`, `NoProductLines`, `InvalidQuantity`, `MissingProductName`, `MissingSet`, `MissingCollectorNumber`, `MissingCondition`, `DuplicateOrder` (FR-006). Required when `Outcome = Rejected`; null when `Succeeded` |
| FailureMessage | string, nullable | Human-readable detail; required when `Outcome = Rejected` |
| ResultingOrderId | FK → Order, nullable | Set when `Outcome = Succeeded`; null when `Rejected` |

## Relationships

```text
ImportAttempt (1) ──── (0..*) ImportOrderResult ──── (0..1) Order ──── (1..*) OrderLine
```

- One `ImportAttempt` → many `ImportOrderResult` (one per order evaluated in that file).
- One `ImportOrderResult` → at most one `Order` (only when it succeeded).
- One `Order` → one or more `OrderLine` (never zero — FR-007), created atomically together (FR-016).

## Validation summary (from FR-005)

An `Order` is rejected — no `Order` row is created for it — when any of:
- Order identifier missing/unreadable → `MissingOrderIdentifier`
- Zero product lines → `NoProductLines`
- Any `OrderLine` missing/unreadable: `ProductName` → `MissingProductName`; `Set` → `MissingSet`; `CollectorNumber` → `MissingCollectorNumber`; `Condition` → `MissingCondition`; `Quantity` not a positive whole number → `InvalidQuantity`
- Duplicate `TcgplayerOrderId` already present → `DuplicateOrder`

`Rarity` and `Variant` absence never triggers rejection.
