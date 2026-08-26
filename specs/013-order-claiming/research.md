# Phase 0 Research: Exclusive Order Claiming

No `NEEDS CLARIFICATION` markers remain in the Technical Context — `/speckit-specify` and the
explicit `/speckit-clarify` pass already resolved every open product question (claim-ending
mechanism, single-active-claim limit, Manager force-release). This research covers the technical
decisions needed to satisfy constitution Principle VI's MUST without inventing product behavior.

## 1. Concurrency-safe exclusive claim (Principle VI MUST)

**Decision**: A single conditional `ExecuteUpdateAsync` call per claim attempt:

```csharp
var rowsAffected = await context.Orders
    .Where(o => o.Id == orderId && o.ClaimedByEmployeeId == null)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(o => o.ClaimedByEmployeeId, actorEmployeeId)
        .SetProperty(o => o.ClaimedAt, DateTimeOffset.UtcNow)
        .SetProperty(o => o.Status, OrderStatus.InProgress),
        cancellationToken);
```

`rowsAffected == 1` means this request won the claim; `rowsAffected == 0` means another request
already claimed it first (or it no longer exists). No row is ever loaded/tracked/saved — this is a
single round-trip, database-enforced compare-and-swap on the `WHERE ClaimedByEmployeeId == null`
predicate, so two concurrent requests targeting the same order can never both see `rowsAffected == 1`.

**Rationale**: The constitution's own EF Core Engineering Standards section recommends
`ExecuteUpdateAsync` set-based operations over load-track-modify specifically to avoid the
read-modify-write race this feature must prevent. A `SELECT` then `SaveChangesAsync()` pattern
(even with EF's optimistic concurrency token) would require a retry loop on `DbUpdateConcurrencyException`
and still does more work than a single conditional `UPDATE`.

**Alternatives considered**:
- *Optimistic concurrency token (`[Timestamp]`/rowversion) + `SaveChangesAsync`*: works, but requires
  loading the entity first and handling `DbUpdateConcurrencyException` on every race — more code for
  the same guarantee `ExecuteUpdateAsync`'s `WHERE` predicate gives in one step.
- *Application-level lock (e.g. `SemaphoreSlim` per order)*: does not work across multiple API
  instances/processes and is unnecessary when the database itself can enforce the invariant.
- *Raw SQL with locking hints (`UPDLOCK`/`ROWLOCK`)*: unnecessary — the conditional `UPDATE`'s `WHERE`
  clause already gives an atomic, race-free compare-and-swap without hand-written SQL.

## 2. "Pick Next Order" candidate selection under contention

**Decision**: Select the oldest `Ready`, unclaimed order (`OrderBy(ImportedAt)`, `AsNoTracking`,
`Take(1)`), then attempt the conditional update from §1 against that specific `Id`. If
`rowsAffected == 0` (another request claimed it in the gap between select and update), re-query
excluding that `Id` and retry, up to a small bounded number of attempts (5) before returning a
"no orders currently available" outcome to the caller.

**Rationale**: At this feature's scale (single shop, a handful of concurrent pickers), true
contention on "the very next order" is rare enough that a select-then-conditional-update-with-retry
loop is simpler and sufficient — it never returns a wrong or duplicate claim, it only occasionally
retries once. This reuses exactly the same atomic primitive from §1, so the "two employees pressing
Pick Next Order MUST NOT both succeed in claiming the same order" guarantee holds even under retry.

**Alternatives considered**:
- *`SELECT ... FOR UPDATE SKIP LOCKED`-style row locking*: not idiomatic EF Core, and unnecessary at
  this scale; the retry loop achieves the same outcome with ordinary EF Core APIs.
- *Claim a randomly-selected candidate from the top N Ready orders*: rejected — FIFO-oldest is the
  documented Assumption in spec.md and matches picker expectation of a first-in-first-out queue.

## 3. Enforcing "at most one active claim per employee"

**Decision**: A database-level filtered unique index on `Orders.ClaimedByEmployeeId`
(`WHERE ClaimedByEmployeeId IS NOT NULL`), configured via
`builder.HasIndex(o => o.ClaimedByEmployeeId).IsUnique().HasFilter("[ClaimedByEmployeeId] IS NOT NULL")`
in `OrderConfiguration`. `OrderClaimService` catches the resulting unique-constraint violation
(mirroring the existing `UniqueConstraintViolationException` catch already used for username
uniqueness in `EmployeeManagementService.CreateAsync`) and maps it to an
`AlreadyHasActiveClaim` outcome.

**Rationale**: This is the same two-tier pattern already established in this codebase for the
analogous "two requests both grab the same unique thing" problem (case-insensitive username
uniqueness): an application-level check for the common case, backed by a database constraint as the
authoritative, race-proof guarantee. It reuses an existing, proven exception-translation path instead
of inventing a new one, and it protects the "single active claim" rule even when the same employee
races themselves from two browser tabs — a scenario the primary conditional-update in §1 does not by
itself prevent, since §1's predicate only checks the *target order's* claim state, not the *actor's*
other claims.

**Alternatives considered**:
- *Application-level check only ("does this employee already have a claim?" query before claiming)*:
  same TOCTOU race as any check-then-act pattern; insufficient on its own given Principle VI's MUST
  standard, so the DB constraint is required regardless — the application-level check remains only as
  a cheap early-exit for the common (non-racing) case.

## 4. Authorization split for claim/release vs. force-release

**Decision**: `claim` (both entry points) and `release` are available to any authenticated employee
(`[Authorize]`, no role restriction — Pickers do the picking); `force-release` is restricted to
`[Authorize(Roles = nameof(EmployeeRole.ManagerAdmin))]`, mirroring the exact attribute already used
on `EmployeesController`. The acting employee ID for claim/release comes from the same
`ActorEmployeeId()` (`ClaimTypes.NameIdentifier`) helper pattern used throughout the codebase.

**Rationale**: Directly reuses existing, proven authorization conventions — no new authorization
mechanism is introduced. This matches spec.md's FR that force-release is a Manager/Admin-only
capability while claim/release are ordinary picker actions.

**Alternatives considered**: None — this is a direct application of an existing pattern, not a new
design decision requiring alternatives.
