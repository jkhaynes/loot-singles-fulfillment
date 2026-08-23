# Implementation Plan: Basic Production Logging

**Branch**: `006-production-logging` | **Date**: 2026-08-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-production-logging/spec.md`

## Summary

Add basic, safe, proportional production logging to the order-import pipeline using ASP.NET Core's
built-in `ILogger<T>`, writing to console/stdout only (no new package, provider, or configuration).
One unified attempt-completion log entry (Information on full success, Warning otherwise, with a
failure-type breakdown) replaces the single pre-existing ad hoc warning, plus one new per-order Error
entry for unexpected persistence failures. No business behavior, API response, or persisted data
changes.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend only; no frontend change)

**Primary Dependencies**: `Microsoft.Extensions.Logging` (already part of the `Microsoft.AspNetCore.App` shared framework via `Microsoft.NET.Sdk.Web`); no new package

**Storage**: N/A — no schema, migration, or persisted-entity change; logs are not persisted

**Testing**: xUnit; new logging-behavior tests belong in `LootSingles.IntegrationTests.Import` against a real, migrated SQL Server Testcontainers database (005-sql-server-migration's infrastructure), using a shared `CapturingLogger<T>` test double (research.md §6), because asserting a correct `ImportId` requires a real, EF Core-assigned identity value

**Target Platform**: ASP.NET Core Web API (unchanged); output consumed via Azure Container Apps' Log Stream in hosted environments, and directly via the console/terminal in local `dotnet run`

**Project Type**: Web application (backend + frontend) — this feature touches backend only

**Performance Goals**: One attempt-level log entry per import attempt regardless of batch size (SC-003); at most one additional entry per order that hits an unexpected persistence failure (an already-rare path)

**Constraints**: `ILogger<T>` only, no third-party/paid logging platform, no custom logging abstraction, console/stdout only, no PII/secret content, no behavior change (constitution Principle XI, as amended; FR-001/002/008/009/010)

**Scale/Scope**: One production file changed (`PackingSlipImportService.cs`), one test-support file changed (`ImportTestSupport.cs`), one existing test file updated (`ParserExceptionSafetyTests.cs`), new test coverage added for the three logged outcomes

## Constitution Check

*GATE: Passed before research and re-checked after design.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Scoped to import only, per explicit clarification; no product behavior added beyond observability. | PASS |
| III Reviewability | Single feature, single production file touched, small diff. | PASS |
| IV TDD | Log level/content assertions are new tests written before the corresponding production change (see tasks.md); a documentation-only constitution/CLAUDE.md update is a non-behavior exception. | PASS |
| IX Replaceable integrations | Uses the application's own `FailureType` vocabulary in logs rather than raw exception text, reinforcing (not weakening) the existing rule that consumers must not depend on exception message text. | PASS |
| XI Reliability/Observability (amended) | This feature is the direct implementation of the amended standard: `ILogger<T>` only, console/stdout only, no paid platform, no custom abstraction, no PII/secrets, proportional volume. | PASS |
| XII Dependencies | Logging call sites stay inside `PackingSlipImportService` (Application layer), using only `Microsoft.Extensions.Logging` abstractions already referenced there; no Infrastructure/UI leakage. | PASS |
| XIII Simplicity | No new abstraction; promotes/reuses one existing private test double instead of duplicating it (research.md §6); one unified log helper instead of four scattered call sites. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `PackingSlipImportService` | Gains one private `LogCompletion(ImportAttempt)` helper and one inline `LogError` call at the existing persistence-failure catch site; still depends only on `ILogger<PackingSlipImportService>` (already injected), `IImportPersistence`, and `IPackingSlipParser`. | No new variation point needed — one decision rule covers all terminal outcomes (research.md §4). Failure is represented by existing `FailureType`/`ImportAttempt` data, not new types. Testable via the existing `CreateDatabaseContext`/`CreateService` test-support pattern. |
| `IImportPersistence` / `ImportRepository` | Unchanged. The one added `SaveChangesAsync` call (research.md §3) uses the existing interface method; no new persistence contract. | No behavior/contract change; existing duplicate-key and persistence-failure translation is reused as-is. |
| `ImportTestSupport` (test-only) | Gains a promoted, extended `CapturingLogger<T>` (records `LogLevel` + structured state, not just message text) alongside its existing `CreateDatabaseContext`/`CreateService`/`CreateDatabaseFreeService` helpers. | Consolidates a duplicate-in-waiting per Principle XIII rather than adding a second private copy; test-only, no production dependency. |
| `appsettings.json` / hosting | No change. Default `Logging:LogLevel` (`"Default": "Information"`) and the framework's default console provider already satisfy every requirement. | Nothing new to fail; verified by inspection (research.md §1), not by adding configuration that didn't need to exist. |

**Post-design gate**: Boundaries remain inward-facing (Application layer owns its own logging decisions), no new abstraction was introduced without a concrete purpose, and the one test-double consolidation is required by Principle XIII rather than optional polish. No deviation is required.

## Project Structure

### Documentation (this feature)

```text
specs/006-production-logging/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output (log entry shapes; no persisted entity)
├── quickstart.md         # Phase 1 output (manual + automated validation)
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

No `contracts/` directory: this feature adds no new public API, CLI, or external interface — it only
changes what an existing internal service writes to its already-injected `ILogger<T>`.

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Application/Import/PackingSlipImportService.cs   # production change
└── tests/LootSingles.IntegrationTests/Import/
    ├── ImportTestSupport.cs            # promote/extend shared CapturingLogger<T>
    ├── ParserExceptionSafetyTests.cs   # use shared double; update one assertion (research.md §6)
    └── ImportLoggingTests.cs           # new — the three logged-outcome scenarios (FR-003/004/005/006)
```

**Structure Decision**: No new project, layer, or abstraction. All production code stays inside the
existing `PackingSlipImportService`; all new/changed tests stay inside the existing
`LootSingles.IntegrationTests.Import` folder alongside the tests they extend or resemble.

## Complexity Tracking

No constitution violations require justification.

## Implementation Phases

1. Promote and extend the shared `CapturingLogger<T>` test double in `ImportTestSupport.cs`
   (captures `LogLevel` and structured state, not just message text); update
   `ParserExceptionSafetyTests` to use it.
2. Write failing tests for the three logged outcomes (full success → Information; partial
   failure/attempt-flagged → Warning with failure-type breakdown; unreadable file → Warning) and
   the per-order persistence-failure Error entry, each asserting `ImportId` is a real, non-zero
   value.
3. Add the eager `ImportAttempt` save (research.md §3) and the unified `LogCompletion` helper plus
   the persistence-failure `LogError` call to `PackingSlipImportService`, replacing the old ad hoc
   warning, to turn the new tests green without changing any existing business behavior.
4. Manually validate via quickstart.md (local run, watch console output for the three scenarios).

## Risks

- **`ImportId` timing**: the eager attempt-save (research.md §3) is the one non-obvious change:
  mitigated by making it the very first task after test coverage exists, so a regression is caught
  immediately by the new tests' `ImportId != 0` assertions.
- **Existing test assertion change**: `ParserExceptionSafetyTests` currently asserts message content
  that will no longer be produced (research.md §6); mitigated by updating that one assertion in the
  same change, with the full existing suite re-run to confirm nothing else depended on the old text.
