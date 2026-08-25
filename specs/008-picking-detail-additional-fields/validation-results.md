# Validation Results: Order Picking Detail — Additional Packing-Slip Fields

**Date**: 2026-08-24
**Branch**: `008-picking-detail-additional-fields`

## Automated validation

| Gate | Command | Result |
|---|---|---|
| Backend build | `dotnet build backend/LootSingles.sln --no-restore` | PASS — 0 warnings, 0 errors |
| Backend unit tests | `dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj --no-build --no-restore` | PASS — 74 passed, 0 failed |
| SQL Server integration tests | `dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj --no-build --no-restore` | PASS — 105 passed, 0 failed |
| C# formatting | `dotnet csharpier check backend` | PASS — 130 files checked |
| Frontend lint | `npm --prefix frontend run lint` | PASS — 0 errors; the same pre-existing `react(only-export-components)` warning in `AuthContext.tsx` already noted in feature 007's validation-results.md |
| Frontend unit/component tests | `npm --prefix frontend test` | PASS — 44 passed, 0 failed |
| Frontend type check | `npx tsc -b` (also runs as part of `npm run build`) | PASS — no errors |
| Frontend production build | `npm --prefix frontend run build` | PASS |
| Frontend formatting | `npm --prefix frontend run format:check` | PASS |
| Browser/E2E suite | `npm --prefix frontend run test:e2e` | PASS — 9 passed, 0 failed |

One issue was found and fixed during this pass: extending `order-detail.spec.ts` (T019) to assert
the seeded line's collector number (`#58/102`) introduced a locator collision — the existing
`line.getByText('2')` calls used for the quantity-emphasis assertions started substring-matching
inside `#58/102` as well as the intended `<strong>2</strong>` quantity element, since Playwright's
`getByText` does substring matching by default. Fixed by adding `{ exact: true }` to those four
locator calls. No production code was affected; this was a test-only fix, and the suite passed
cleanly (9/9) after the correction.

T020 (mobile-viewport check) passed without any `OrderDetailPage.css` change — the existing
auto-flowing attribute grid already accommodated the three added rows and the header's added
status line at a 390×844 viewport.

## Quickstart validation

The application was run locally (backend on `https://localhost:7166` / `http://localhost:5098`
via the `https` launch profile, frontend via `npm run dev` on `http://localhost:5173`, LocalDB for
the dev database) and exercised interactively in the browser by the Developer during this feature's
development, ahead of this formal write-up. Observations below are consolidated from that session
plus the automated Playwright/component coverage that independently re-verifies the same scenarios.

1. **Product line and collector number always shown (US1, FR-001/FR-002, SC-001) — PASS.** Every
   product line displayed its game (e.g., "Pokemon") and collector number (e.g., "#58/102",
   "#067/086" in component tests) alongside the existing fields. Verified live and by
   `OrderDetailPage.test.tsx` and `order-detail.spec.ts`.
2. **Rarity shown when present, omitted cleanly when absent (US1, FR-003, SC-003) — PASS.** A line
   with a recorded rarity ("Double Rare") displayed it; a line with no recorded rarity (the seeded
   E2E order's Pikachu line, and the component-test Pikachu line) rendered cleanly with no blank or
   broken rarity field and no "Rarity" label present.
3. **Order status visible (US2, FR-004, SC-002) — PASS.** The order's status ("ready") appeared in
   the detail view's header next to its TCGplayer identifier, without navigating back to a list
   view. Verified live and by the dedicated component test and `order-detail.spec.ts`.
4. **Still read-only (FR-005) — PASS.** No claim, pick, complete, or other state-changing control
   was present anywhere on the detail view — unchanged from feature 007.
5. **Still usable at mobile width (FR-006, SC-004) — PASS.** At a 390×844 touch viewport, all line
   information — including the three added fields and the order status — remained visible and
   `document.documentElement.scrollWidth` did not exceed `clientWidth`.

## Architecture and Changeability Review

| Review dimension | Assessment | Result |
|---|---|---|
| Responsibility | `OrderDetail`/`OrderLineDetail` remain pure read projections; `OrderRepository.GetByIdAsync` remains a single `AsNoTracking()` query; `OrdersController` still only maps records to HTTP shapes; the page still only owns presentation state. No component absorbed a new responsibility. | PASS |
| Boundaries | The four new fields stayed within the same layers feature 007 established — Application (read model), Infrastructure (EF projection), Api (response DTOs), frontend feature module (types + rendering). No boundary was crossed or blurred. | PASS |
| Dependency direction | Unchanged — Api and Infrastructure still depend on Application contracts; Application still has no persistence or UI dependency. | PASS |
| Foreseeable variation | The next additional detail field (e.g., a future data point) would follow the exact same four-file pattern this change used — no unrelated orchestration or central branching logic needed to change. | PASS |
| Extension cost | Confirmed empirically: adding four fields required touching exactly the four files plan.md predicted (`OrderDetail.cs`, `OrderRepository.cs`, `OrdersController.cs`, `ordersApi.ts`/`OrderDetailPage.tsx`) plus their tests — no other file needed modification. | PASS |
| Failure modeling | No new failure mode was introduced — `ProductLine`/`CollectorNumber` are non-nullable and always present per the domain model; `Rarity` reuses the exact `null`-omission handling `Variant` already established; `Status` is a closed enum with existing serialization. | PASS |
| Domain integrity | Still a pure read of already-valid persisted `Order`/`OrderLine` entities — no new external input or construction path was introduced. | PASS |
| Testability | Backend: extended one existing integration test rather than adding parallel infrastructure. Frontend: extended existing component tests and one E2E scenario. No new test infrastructure was needed. | PASS |
| Abstraction rationale | No new interface, service, repository, or component was introduced anywhere in this feature — purely additional properties on records/DTOs/types and additional rendered fields. | PASS |
| Simplicity | The entire feature was four backend files, four frontend files (plus their tests), and zero schema changes — matches plan.md's "no new project, layer, endpoint, route, or component" structure decision exactly. | PASS |

### Review outcome

No Must Fix or Advisory findings were identified. No remediation tasks were appended to
`tasks.md`. The implementation remains read-only, exposes no customer PII (all four added fields
are product/order metadata), and adds no production logging — consistent with research.md §5 and
the precedent established by features 006/007 for this class of routine, no-side-effect read.
