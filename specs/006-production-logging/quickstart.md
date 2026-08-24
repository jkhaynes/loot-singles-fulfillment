# Quickstart: Validate Basic Production Logging

No new service, package, or configuration is introduced. This validates that the existing local run experience now surfaces useful, safe log output during order import.

## Prerequisites

- .NET SDK 10.0.x
- A running SQL Server-family database reachable via `ConnectionStrings:LootSingles` (see `specs/005-sql-server-migration/README.md` setup, or use an already-configured local/dev database) with migrations applied
- Frontend dependencies installed (`npm --prefix frontend install`) if validating through the UI rather than direct HTTP calls

## Run the API locally and watch the console

```powershell
dotnet run --project backend/src/LootSingles.Api
```

Leave this terminal window visible — it **is** the log viewer. With no external sink configured (FR-001/FR-002), every log line this feature adds prints directly to this console via the ASP.NET Core default console logging provider. In Azure Container Apps, this same stdout stream is what the Log Stream feature displays; locally, it's just this terminal.

## Exercise the three logged outcomes

Import a packing-slip PDF through the frontend (`npm --prefix frontend run dev`, log in, use the import screen) or directly via `POST /api/imports` (multipart form, field name `file`). Use fixtures under `backend/tests/LootSingles.IntegrationTests/Fixtures/PackingSlips/` for repeatable cases, or any real packing slip.

1. **Fully successful import** — import a clean multi-order batch (e.g., `valid-multi-order-batch.pdf`).
   **Expected**: one `Information`-level console line reporting the import's id and the number of orders detected/succeeded, and nothing else added by this feature.

2. **Partially failed / attempt-flagged import** — import a batch containing a rejected order (e.g., `partial-batch-one-bad-order.pdf`) or re-import the same batch twice to trigger a duplicate-order rejection.
   **Expected**: one `Warning`-level console line reporting the import's id, detected/succeeded/failed counts, and a breakdown of which failure type(s) occurred (e.g., a `DuplicateOrder` count).

3. **Unreadable file** — import a non-packing-slip PDF or a corrupted file (e.g., `not-a-packing-slip.pdf`, `corrupted-file.pdf`).
   **Expected**: one `Warning`-level console line reporting the import's id and `UnreadablePdf`, with no other log entry for that attempt.

## Confirm nothing unsafe is printed

While performing the above, inspect the console output and confirm it never contains: a customer's name or address, any product/card description text from the packing slip, a password, PIN, token, or connection string. Only identifiers, counts, statuses, and failure-type names should appear (data-model.md).

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
```

Expected: all existing tests continue to pass unchanged (SC-004), plus new tests (in `LootSingles.IntegrationTests.Import`) asserting the log level and structured field values for each of the three scenarios above, using the shared `CapturingLogger<T>` test double (research.md §6) against a real, migrated SQL Server Testcontainers database so `ImportId` reflects a real, non-zero identity value.
