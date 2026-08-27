# Quickstart: Validate Exclusive Order Claiming

This validates that "Pick Next Order" and "Choose Order" both exclusively and safely claim an
order, that every employee can see who is working an order, that a picker can release their own
claim, and that a Manager/Admin can force-release anyone's claim — with no window in which two
employees can end up holding the same order.

## Prerequisites

- Same as feature 009's quickstart: .NET SDK 10.0.x, Node.js, a running SQL Server-family database
  with migrations applied (including this feature's migration adding `ClaimedByEmployeeId` /
  `ClaimedAt` and the filtered unique index — data-model.md)
- At least two Ready, unclaimed orders imported/seeded
- At least two employee accounts: one Picker, one Manager/Admin (reuse feature 002's seeded
  accounts)

## Run the app

```powershell
dotnet run --project backend/src/LootSingles.Api
npm --prefix frontend run dev
```

Log in as the Picker account first.

## Manual validation

1. **Pick Next Order assigns exclusively** (US1, FR-001, SC-001): with at least one order
   available, press "Pick Next Order" and confirm you land on that order's detail view, shown as
   in progress and claimed by you.
2. **No orders available is a clear outcome** (US1 AC3, FR-006, SC-004): claim every available
   order (release them afterward to restore state), then press "Pick Next Order" again and confirm
   a clear "no orders available" message — not an error page or an indefinite spinner.
3. **Choose Order respects an existing claim** (US2, FR-002/FR-004, SC-002): while one order is
   claimed (by this Picker or another), open the order list in a second browser session (or a
   different employee), confirm the claimed order is visibly "In Progress · Picking by <name>"
   (US3, FR-005, SC-003), and confirm attempting to open/claim it is rejected while an unclaimed
   order can still be chosen successfully.
4. **Two employees racing never both win** (US1 AC2 / US2 AC2, FR-003, SC-001/SC-002): with two
   logged-in sessions (two browsers or a normal + incognito window) as two different employees,
   with at least two orders available, click "Pick Next Order" in both at approximately the same
   time and confirm they land on two different orders — repeat a few times, including one session
   using "Choose Order" against the same target the other is "Pick Next Order"-ing, per Edge Cases
   §2.
5. **One active claim per employee** (US1 AC4 / US2 AC3, FR-009, SC-007): while already holding a
   claim, attempt "Pick Next Order" and "Choose Order" (on a different order) and confirm both are
   rejected with a clear "release your current order first" message.
6. **Release frees the order** (US4, FR-008, SC-005): release your claimed order and confirm it
   becomes available again in the order list, and that a different employee can now claim it.
7. **Only the claimant (or a manager) can release** (US4 AC2): attempt to release an order claimed
   by a different employee and confirm it is rejected.
8. **Manager force-release** (US5, FR-010, SC-006): log in as the Manager/Admin account, force-release
   an order claimed by the Picker, and confirm it becomes available and the Picker no longer holds
   it. Confirm a non-manager attempting force-release is rejected (US5 AC2).
9. **Claims survive logout** (Edge Cases §3, FR-008): claim an order, log out, log back in as the
   same employee, and confirm the claim is still held (no automatic expiry) — release it to restore
   state, or force-release as the manager.

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm --prefix frontend run lint
npm --prefix frontend run test
npm --prefix frontend run build
npm --prefix frontend run format:check
npm --prefix frontend run test:e2e
```

Expected: a genuine concurrent-request test (two simultaneous claim attempts against the same order,
using real `Task.WhenAll` over a relational test database — not a stub that completes synchronously,
per feature 009's `ConcurrencyTrackingHandler` precedent) proves exactly one request wins and the
other receives `order_already_claimed`/`no_orders_available`; `OrderClaimServiceTests` covers every
outcome in contracts/order-claiming-api.md (success, already-claimed, no-orders-available,
employee-already-has-a-claim, not-your-claim, order-not-claimed, forbidden-for-non-manager); the
Playwright suite (`order-claiming.spec.ts`) covers claim → see-as-in-progress-elsewhere → release,
and the Manager force-release path, using two authenticated browser contexts.
