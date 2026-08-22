# Quickstart Validation: Order Import UI

## Prerequisites

.NET 10, Node/npm, Playwright browsers, reachable configured SQL Server, an employee account, and fixture PDFs in `backend/tests/LootSingles.Fixtures/PackingSlips/`.

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/LootSingles.sln
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run test:e2e
```

Expected: backend/frontend suites pass, build/typecheck succeeds, and Playwright desktop/mobile flows have no horizontal overflow.

## End-to-end scenarios

1. Sign in as either role and upload `partial-batch-one-bad-order.pdf`. Observe `InProgress` advance once per completed order—not for parsed pages or product lines—then terminal `Completed`, distinct successes, and a rejection with a specific reason. No customer information appears.
2. Upload `valid-multi-order-batch.pdf` twice. The second run reports typed duplicate rejections. Upload `corrupted-file.pdf` and a non-PDF: the former is an unreadable-file result; the latter is rejected before import.
3. Upload `summary-mismatch.pdf`. A distinct batch warning remains visible alongside per-order outcomes.
4. Open browse after success. The order appears without another manual action with identifier, actual status, and import time. A clean database renders an explicit empty state.
5. Run the integration/frontend case that causes a server-side infrastructure failure while the stream remains writable. It receives terminal `Failed`, distinct from ordinary completed rejection outcomes.
6. Run the interrupted-import integration case: allow at least one order to commit, abort the HTTP request, verify cancellation reaches the import operation, then re-upload the same PDF. The first committed order exists exactly once, no partial Order/OrderLine graph exists, and all remaining valid orders import exactly once; previously committed identifiers are reported as duplicates.
7. Run the frontend stream case that ends before terminal `Completed`/`Failed`. The UI derives `Interrupted`, says the connection was lost and some orders may already have imported, and enables retry rather than claiming zero results.
8. Repeat critical flows at phone and desktop Playwright viewports. Long identifiers/reasons wrap; navigation, controls, results, and browse items stay usable without overlap or horizontal scrolling.
9. Submit files exactly at and just over 25 MB. The boundary file is accepted for normal parser evaluation; the oversized file receives the documented size-limit response before the import service runs and creates no orders.

For scenario 5, any completed-order results remain visible under a prominent Failed label, the UI explains those orders remain imported, and retry is available. For scenario 7, the retained snapshot is explicitly incomplete and potentially stale rather than authoritative.

See [contracts/import-api.md](contracts/import-api.md), [contracts/orders-api.md](contracts/orders-api.md), and [data-model.md](data-model.md) for expected shapes and invariants.
