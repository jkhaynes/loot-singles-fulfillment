# Research: Basic Production Logging

## 1. Delivery mechanism (console/stdout only)

**Decision**: Rely entirely on the ASP.NET Core default logging providers already active in `LootSingles.Api` (a `Microsoft.NET.Sdk.Web` project using `WebApplication.CreateBuilder(args)`). No new NuGet package, provider registration, or `appsettings.json` change is required.

**Rationale**: `WebApplication.CreateBuilder` registers Console (plus Debug/EventSource) logging providers by default, and `Program.cs` contains no call that clears or replaces them. `appsettings.json`'s existing `Logging:LogLevel` section (`"Default": "Information"`) already allows Information, Warning, and Error entries through in the normal running configuration, satisfying FR-002/FR-011 with zero configuration change. Azure Container Apps' Log Stream reads directly from the container's stdout, so nothing further is needed for logs to reach it.

**Alternatives considered**: Explicitly re-registering/pinning the console provider in `Program.cs` for clarity — rejected as unnecessary code for behavior that is already the framework default and already verified present; adding a package (Serilog, etc.) — explicitly excluded by the Product Owner's request and FR-001.

## 2. Where new production code changes live

**Decision**: All production-code changes are confined to `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`, which already receives `ILogger<PackingSlipImportService>` via constructor injection and already contains the project's only existing log statement. No controller, repository, or DbContext change is needed.

**Rationale**: Per clarification, this feature's scope is the import pipeline only. `ImportsController` only orchestrates HTTP framing (multipart parsing, NDJSON streaming) around calls into `IPackingSlipImportService`; it has no business outcome of its own to log distinctly from what `PackingSlipImportService` already knows. `OrdersService`/`DashboardService` are out of scope per clarification.

**Alternatives considered**: Logging from `ImportsController` instead of/in addition to the service — rejected; it would either duplicate the same attempt outcome the service already knows, or require passing structured failure data back up through the streaming NDJSON contract for no benefit, adding complexity FR-009's "avoid excessive logging" and Constitution XIII (no speculative abstraction) argue against.

## 3. Making `ImportId` reliably non-zero for every log statement

**Finding**: `ImportAttempt.Id` is a database-generated identity value. In the current implementation, `IImportPersistence.AddImportAttempt(attempt)` only stages the entity in the change tracker (`context.ImportAttempts.Add(attempt)`); the attempt's `Id` is not assigned until the *first* `SaveChangesAsync()` call that successfully commits it. Today, that first save can happen well into the per-order loop (or not until `CompleteAttemptAsync` for an unreadable file). If the very first order in a batch throws `OrderPersistenceException` from the same `SaveChangesAsync()` call that would have first persisted `attempt`, the whole transaction rolls back and `attempt.Id` remains `0` — the FR-006 error log for that exact order would otherwise report an ID that never actually identifies a persisted attempt row.

**Decision**: Call `persistence.SaveChangesAsync(cancellationToken)` once, immediately after `persistence.AddImportAttempt(attempt)`, before parsing begins. This persists the (as-yet-outcome-less) `ImportAttempt` row on its own and guarantees `attempt.Id` is a valid, stable, non-zero identifier for every subsequent log statement and every subsequent `SaveChangesAsync` call in that same import.

**Rationale**: This is the smallest change that removes the possibility of a misleading `ImportId: 0` in a log entry. It does not change persisted data content or any user-visible/API behavior (FR-010) — the same `ImportAttempt` row ends up with the same final field values; only the timing of its first (now two-step) write changes, which is an internal persistence-sequencing detail, not business behavior. `ImportAttempt.StartedAt` is already set before this call, so the persisted row is well-formed at every point.

**Alternatives considered**: A client-generated correlation identifier (e.g., a `Guid`) independent of the database identity — rejected because it would introduce a second, parallel identifier scheme for the same concept the codebase already has a stable identifier for (`ImportAttempt.Id`, already used as the `ImportAttemptId` foreign key throughout `ImportOrderResult`), which Constitution XIII's simplicity principle argues against absent a concrete need (e.g., pre-persistence correlation, which this feature does not require). Leaving `ImportId` occasionally `0` — rejected as it would produce a misleading, non-identifying log value in exactly the scenario (a first-order persistence failure) where accurate identification matters most.

## 4. One unified completion log instead of scattering log calls across every exit point

**Finding**: `ImportAsync` has four places that can be its terminal outcome: (a) an unhandled parser exception, (b) `parsed is null` with no exception (parser ended without a completion signal), (c) zero recognizable `OrderBlocks`, and (d) the normal end of the per-order loop (which may also carry an attempt-wide `SummaryMismatch` flag alongside normal per-order results). The existing code has exactly one ad hoc log call, placed inside the parser-exception catch block *before* `attempt.Id` is ever assigned (see §3) and using the raw .NET exception type name rather than the application's own `FailureType` vocabulary — which also conflicts with Constitution IX's rule that consumers must not depend on human-readable exception text for behavior-relevant information.

**Decision**: Replace the single ad hoc log call with one private helper, `LogCompletion(ImportAttempt attempt)`, invoked at every terminal point *after* the attempt has been persisted (see §3) — i.e., after `CompleteAttemptAsync` for the early-return unreadable-file paths, and once at the natural end of the per-order loop. The helper applies one rule set:

- **Warning**, with `ImportId` and `FailureType`, when `attempt.AttemptFailureCode` is set and no orders were evaluated (`ImportOrderResults.Count == 0`) — covers unreadable/unparseable files (FR-005).
- **Warning**, with `ImportId`, detected/succeeded/failed counts, and a breakdown of failed-order counts grouped by `FailureType` (including when `AttemptFailureCode == SummaryMismatch` alongside otherwise-normal per-order outcomes), when at least one order failed or `AttemptFailureCode` is set — covers FR-004 and extends its intent to the `SummaryMismatch` attempt-wide condition, which the spec's "important... failures" framing implies but does not name individually.
- **Information**, with `ImportId` and detected/succeeded counts, when every order succeeded and no attempt-wide failure code is set — covers FR-003.

**Rationale**: A single decision point is easier to keep correct and complete than four scattered call sites, satisfies FR-009 (bounded log volume — always exactly one attempt-level entry), and naturally fixes the `ImportId` timing problem in §3 since every call site is already past the eager save.

**Alternatives considered**: Keeping the original per-call-site ad hoc logging and only patching the `ImportId` bug — rejected; it would leave four subtly different, harder-to-verify log statements instead of one, testable decision rule, and would not resolve the `SummaryMismatch` gap or the exception-text/`FailureType` inconsistency.

## 5. Individual per-order Error log for unexpected persistence failures

**Decision**: In the existing `catch (OrderPersistenceException)` block (the branch already distinct from the ordinary `catch (UniqueConstraintViolationException)` duplicate-order path), add one `logger.LogError` call carrying `ImportId`, the order's own `TcgplayerOrderId` (from `block.OrderIdentifier` — the only identifier available, since the order's own database `Id` was never assigned when its save failed), and `FailureType.PersistenceFailure`.

**Rationale**: This is the one case FR-006 calls out as distinct from ordinary, expected per-order outcomes: it signals an infrastructure-level problem (not a data-quality rejection), so it gets immediate, individual visibility rather than waiting to be counted in the attempt-level summary breakdown (§4) — it is still also included in that summary's failure-type breakdown, it is not *instead of* the summary entry.

**Alternatives considered**: Deferring visibility to the attempt-level summary only — rejected; per FR-006 and User Story 1's acceptance scenario 3, this failure type specifically needs its own identifying entry since it represents an unexpected fault, not a routine rejection.

## 6. Test strategy for asserting log level and structured content

**Finding**: A private `CapturingLogger<T>` test double already exists, nested inside `backend/tests/LootSingles.IntegrationTests/Import/ParserExceptionSafetyTests.cs`. It implements `ILogger<T>` and records only the formatted message string and exception — it does not capture `LogLevel` or the structured state (the `{ImportId}`, `{FailedCount}`, etc. name/value pairs `Microsoft.Extensions.Logging` exposes via the `state` parameter as `IReadOnlyList<KeyValuePair<string, object>>` when a standard message-template formatter is used). This feature's tests need to assert both level and individual structured field values, and a second, differently-shaped private copy would duplicate a concept the codebase already has one instance of.

**Decision**: Promote and extend `CapturingLogger<T>` into `backend/tests/LootSingles.IntegrationTests/Import/ImportTestSupport.cs` (the existing shared test-support class for this test folder, which already centralizes `CreateDatabaseContext`, `CreateService`, `CreateDatabaseFreeService`, `OpenFixture`, and `ImportFixtureAsync`). The promoted version additionally records `LogLevel` and exposes the structured key/value pairs from `state`. Update `ParserExceptionSafetyTests` to use the shared version instead of its own nested copy, adjusting its one assertion to match the new unified completion-log content from §4 (it no longer asserts an exception-type-name substring, since that detail is intentionally no longer logged; it instead asserts `LogLevel.Warning`, the attempt's `ImportId`, and `FailureType.UnreadablePdf`, while continuing to assert the customer-supplied secret string never appears in any captured entry).

**Rationale**: Per Constitution XIII, once a second concrete use case needs the same kind of test double an existing one already partially solves, the design must consolidate rather than duplicate. New logging-behavior tests need a real, EF Core-assigned `ImportAttempt.Id` to assert against (see §3), so they belong alongside `ParserExceptionSafetyTests` using `ImportTestSupport.CreateDatabaseContext()` (SQL Server Testcontainers, per 005-sql-server-migration's established isolated-database test infrastructure) rather than the database-free `FakeImportPersistence` path (whose `AddImportAttempt`/`SaveChangesAsync` do not simulate identity assignment and are intended for pure parsing/orchestration tests unrelated to persisted identifiers).

**Alternatives considered**: A brand-new, separate capturing-logger type for this feature's tests — rejected as direct, unnecessary duplication of an existing, extensible test double. Testing log content against the database-free fake — rejected because it cannot produce a real, non-zero `ImportId`, so it cannot verify the exact behavior this feature is about.
