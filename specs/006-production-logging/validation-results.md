# Validation Results: Basic Production Logging

## Phase 5 T017 — no new logging package or provider

- Searched every `backend/**/*.csproj` for `Serilog`, `Application Insights`/`Microsoft.ApplicationInsights`,
  `Log Analytics`, `Seq`, and `Datadog`: zero matches.
- Reviewed `backend/src/LootSingles.Api/Program.cs`: contains no `ClearProviders()`, `AddProvider()`,
  or any third-party logging registration. Only `WebApplication.CreateBuilder(args)`'s default
  ASP.NET Core providers (Console/Debug/EventSource) are active, matching research.md §1.

## Phase 5 T018 — real DI-configured logger levels

Added `RealDiConfiguredLogger_HasInformationWarningAndErrorEnabled` in
`ImportLoggingTests.cs`, resolving `ILogger<PackingSlipImportService>` from an
`AuthWebApplicationFactory`-hosted instance of `LootSingles.Api`. Confirmed
`IsEnabled(LogLevel.Information)`, `IsEnabled(LogLevel.Warning)`, and
`IsEnabled(LogLevel.Error)` are all `true` under the application's actual `appsettings.json`
(`Logging:LogLevel:Default = "Information"`), satisfying FR-011.

## Phase 5 T019 — proportional log volume at scale

Added `ImportAsync_TwoHundredOrderBatch_ProducesExactlyOneAttemptLevelLogEntry`, reusing the
`SyntheticProgressiveParser` pattern from `ProgressReportingTests` to synthesize a 200-order,
all-succeed batch. Confirmed exactly one `Information`-level completion log entry is produced
regardless of batch size, satisfying SC-003 and FR-009's proportionality requirement.

## Phase 5 T020 — full regression

Commands run from the repository root:

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
```

Results:

- Backend solution build: 0 warnings, 0 errors.
- Backend unit suite: 74 passed, 0 failed.
- Backend SQL Server integration suite: 99 passed, 0 failed (disposable Testcontainers SQL Server;
  no Azure contact).
- CSharpier: 129 files checked, all already formatted.

## Phase 5 T021 — manual quickstart validation

**Status: performed by the Developer**, running the API locally (`dotnet run --project
backend/src/LootSingles.Api --launch-profile https`) against the real Azure SQL dev database, with
the frontend (`npm --prefix frontend run dev`) driving the import UI, per `quickstart.md`. All three
logged outcomes were exercised and confirmed:

- **Unreadable file**: console showed `warn: LootSingles.Application.Import.PackingSlipImportService[0]
  Import attempt 1 failed before any orders could be evaluated: UnreadablePdf. Detected 0, succeeded 0,
  failed 0.` — Warning level, `ImportId`/`AttemptFailureType` present, no file content.
- **Partial failure**: console showed `Import attempt 8 completed with failures. Detected 1,
  succeeded 0, failed 1. MissingProductName: 1 [MISSING-PRODUCT-FIXTURE]` (after the T027 labeling
  fix below) — Warning level, counts, failure-type breakdown, and the affected order's TCGplayer
  identifier all present and legible from console output alone.
- **Fully successful import**: confirmed by the Developer to produce the expected single
  Information-level entry with no other log noise.

No unsafe content (customer name/address, packing-slip content, PIN, connection string) was observed
in any log line across the three scenarios.

Two issues were found and fixed during this manual run, both unrelated to the feature's core log
content and recorded separately: (1) a pre-existing local dev-environment mismatch between Vite's
proxy target and the backend's `UseHttpsRedirection()` (fixed in `frontend/vite.config.ts`, tracked
as a separate change outside this feature's scope), and (2) the T027 message-labeling gap below
(tracked as part of this feature).

## Phase 6 — per-order identifiers in the failure-type breakdown

During T021's manual validation, the Developer clarified that the failure-type breakdown should
include each affected order's TCGplayer order identifier (not just a count), so a specific failed
order can be looked up directly (e.g., in TCGplayer) without a database query — for per-order
failures within an otherwise-readable attempt, uncapped. This amends `FR-004`/`SC-001` (recorded in
spec.md's Clarifications section) and `data-model.md`'s failure-type breakdown row.

- **T023/T024 Red**: extended `ImportAsync_DuplicateOrderReimport_ProducesWarningWithDuplicateBreakdown`
  to assert a `DuplicateOrderIds` field, and added
  `ImportAsync_OrderWithMissingIdentifier_ProducesWarningWithMissingIdentifierPlaceholder` (using
  `missing-order-identifier.pdf`) asserting `MissingOrderIdentifierIds = "(missing)"`. Both failed
  for the expected reason: no `*Ids` field existed in the breakdown.
- **T025 Green**: `LogCompletion`'s breakdown loop now appends one `{FailureType}Ids` argument per
  group alongside its existing `{FailureType}Count`, joining `SourceOrderIdentifier` values with
  `,` and substituting the literal `(missing)` for any null/whitespace identifier (the
  `MissingOrderIdentifier` case, where the order's own identifier is what's missing).
- **T026 regression**: both new/extended tests pass. Full suite: 74 unit passed, 100 SQL Server
  integration passed (0 failed), `dotnet csharpier check backend` reports all 129 files already
  formatted.

## Phase 6 T027 — failure-type message labeling (found during T021 manual validation)

Real console output during T021 showed `Import attempt 8 completed with failures. Detected 1,
succeeded 0, failed 1. 1 MISSING-PRODUCT-FIXTURE` — the count and order identifier were present but
unlabeled, so a human reading raw console output could not tell "1 MISSING-PRODUCT-FIXTURE" meant
`MissingProductNameCount=1, MissingProductNameIds=MISSING-PRODUCT-FIXTURE`. The default ASP.NET Core
console formatter only ever prints the formatted message text, never the named structured arguments
separately, so this was a genuine SC-005 gap despite the structured state itself being complete.

Fix: `LogCompletion`'s breakdown loop now prepends the failure-type name as a literal label —
`{FailureType}: {Count} [{Ids}]` — producing e.g. `DuplicateOrder: 13 [F0000010-ABC010-00010,...]` and
`MissingOrderIdentifier: 1 [(missing)]`. Extended the T023 tests to assert this labeled text. Full
regression: 74 unit passed, 100 SQL Server integration passed, `dotnet csharpier check backend`
reports all 129 files formatted.

## Phase 7 — /code-design-review remediation (T028/T029)

`/code-design-review` found 2 Must Fix and 3 Advisory findings. Must Fix #2 (the unrelated
`frontend/vite.config.ts` commit on this branch) was accepted as-is by the Developer and left
unaddressed by design — no task was created for it. The remaining findings were resolved:

- **T028 (Must Fix #1)**: added `ImportAsync_SummaryMismatchWithNoFailedOrders_ProducesWarningNotInformation`,
  importing `summary-mismatch.pdf` (one order, no per-order failures) and asserting the completion
  log is `LogLevel.Warning` with `AttemptFailureType == FailureType.SummaryMismatch`, guarding
  `LogCompletion`'s branch-2 guard against a future regression to `Information`. Passed immediately
  against the existing implementation, as expected (no production code change).
- **T029 (Advisory A3)**: added a comment above `LogCompletion`'s dynamic `StringBuilder` template
  construction explaining why it isn't a static `ILogger` template (variable-cardinality
  per-`FailureType` breakdown), so a future maintainer doesn't "simplify" it back.

Full regression: 74 unit passed, 101 SQL Server integration passed (0 failed), `dotnet csharpier
check backend` reports all 129 files formatted.

## Phase 8 — convergence remediation (T030/T031)

`/speckit-converge` found 2 gaps against the full spec/plan/tasks inventory:

- **T030 (partial, HIGH — US1/AC1)**: no test verified the Warning completion log's structured
  state with both `OrdersSucceeded > 0` and `OrdersFailed > 0` in the same attempt (a genuine
  mixed-outcome batch, per US1's literal acceptance scenario). Added
  `ImportAsync_MixedSucceededAndFailedOrders_ProducesWarningWithBothNonZeroCounts`, importing
  `partial-batch-one-bad-order.pdf` (3 orders: 2 succeed, 1 fails with `MissingOrderIdentifier`,
  per the existing `PartialImportTests` fixture behavior) through the `CapturingLogger`, asserting
  `OrdersDetected=3`, `OrdersSucceeded=2`, `OrdersFailed=1`, the `MissingOrderIdentifierCount`/`Ids`
  breakdown, and the labeled message text. Passed immediately — confirms this was a coverage gap,
  not an implementation defect.
- **T031 (unrequested, LOW)**: `frontend/vite.config.ts`'s HTTPS dev-proxy fix (commit `ee0d3e2`)
  traces to no task in this feature's spec/plan/tasks. Resolution: **kept on this branch**, per the
  Developer's explicit decision during `/code-design-review` ("I'm ok with this for this
  situation") — no further action taken.

Full regression: 74 unit passed, 102 SQL Server integration passed (0 failed), `dotnet csharpier
check backend` reports all 129 files formatted.

## Phase 5 T022 — constitution Architecture and Changeability Review

Evaluated every component this feature touched against the plan's original architecture table
(`plan.md`) and the constitution's ten review points:

- **`PackingSlipImportService.LogCompletion`**: single responsibility (decide and emit exactly one
  attempt-completion log entry, per the research.md §4 decision rule). Depends only on the already-
  injected `ILogger<PackingSlipImportService>` and the persisted `ImportAttempt`/`ImportOrderResult`
  domain data already available at each call site — no new dependency. The three branches partition
  cleanly and exhaustively over the domain's actual state combinations (verified by inspection:
  `UnreadablePdf` only ever coincides with zero evaluated orders; `SummaryMismatch` only ever
  coincides with a non-zero evaluated-order count), so there is no unreachable or ambiguous branch.
  The dynamic per-`FailureType` breakdown (`StringBuilder` + positional `object?[]` args) is less
  idiomatic than a static `ILogger` message template, but it is the direct, justified consequence of
  data-model.md's already-approved requirement ("one named argument per type present... only for the
  types actually present") — a static template cannot express a variable-cardinality field set, and
  the alternative (always logging every `FailureType`'s count, zero or not) would contradict that
  approved shape. No simpler design satisfies the same approved requirement.
- **Eager `SaveChangesAsync` after `AddImportAttempt`**: isolated, single-purpose (research.md §3),
  confined to the same method that already owned the attempt's lifecycle. Verified it does not
  introduce a new unguarded failure surface: the controller (`ImportsController.StreamSnapshotsAsync`)
  already wraps the entire `ImportAsync` enumeration in a catch-all that returns a clean `500` before
  any response streaming has started, which is exactly the state at this call site.
- **Per-order persistence-failure `LogError`**: confined to the existing, already-distinct
  `catch (OrderPersistenceException)` branch; uses only the application's own `FailureType` vocabulary
  and the order's source identifier, never exception message text (Constitution IX) and never a raw
  packing-slip field (Constitution VII, data-model.md's exclusion list).
- **`ImportTestSupport.CapturingLogger<T>`**: promoted from a private nested type into the shared
  test-support class per Constitution XIII's duplication-consolidation rule, extended (not
  reimplemented) to also capture `LogLevel` and structured `state`. No production dependency; test-only.
- **No new abstraction, interface, package, or configuration was introduced.** `appsettings.json`'s
  existing `Logging:LogLevel:Default = "Information"` and the framework's default console provider
  already satisfy every requirement (T017, T018).
- **Testability**: verified at three independent levels — unit-of-work behavior against a real,
  migrated SQL Server database with a capturing logger (T004–T007, T014, T019), the real DI-composed
  logger's enabled levels via `WebApplicationFactory` (T018), and full-suite non-regression (T020).
- **Failure modeling**: every log statement sources its content exclusively from already-typed domain
  concepts (`FailureType`, `ImportOutcome`, counts) already established by feature 001, not from
  exception message text or ad hoc strings — consistent with Constitution IX/XII.
- **Domain integrity / sensitive content**: confirmed by inspection and by T015 that no log statement
  references `ImportOrderResult.FailureMessage` or `ImportAttempt.AttemptFailureMessage` (which may
  contain order identifiers or constructed text) — only `FailureType`/`AttemptFailureCode` enum values,
  identifiers, and counts are logged, matching data-model.md's explicit inclusion/exclusion lists.

**No Must Fix findings.** No new task is required. Convergence verification may proceed once T021's
manual validation is reported back by the Developer.
