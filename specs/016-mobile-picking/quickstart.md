# Quickstart: Validating Mobile Picking Experience

**Feature**: 016-mobile-picking | **Date**: 2026-09-20

How to run and prove each user story. Shapes and rules live in
[data-model.md](data-model.md) and [contracts/](contracts/) — this file is the run guide.

## Prerequisites

- .NET 10 SDK, Node 20+
- **Docker running** — the integration tests use Testcontainers SQL Server and fail
  confusingly without it
- An imported order spanning **at least two games and two sets per game**. This is the fixture
  the whole feature depends on; an order from a single set exercises almost none of it.

## Run the suites

```bash
# Backend — unit, then integration (integration needs Docker)
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj

# Frontend
cd frontend && npm test

# E2E — builds and starts its own API and web hosts
cd frontend && npx playwright test
```

> Two traps recorded from feature 015, both of which cost real debugging time:
> **rebuild the E2E host explicitly** rather than trusting a stale binary, and make sure the
> dev backend is not already bound to the E2E host's ports.

## Story 1 — Walk to each storage box once

1. Open an order spanning two games and several sets.
2. Confirm all products of one game appear before any product of the other.
3. Confirm games are in alphabetical order, and sets alphabetical within each game.
4. Confirm every product of a set is contiguous.
5. Confirm the product and card counts on each set header — the card count must exceed the
   product count wherever a line has quantity greater than one.

**Passes when** the order is walkable box by box, and no line has disappeared. Count the lines
against the order total; totality is the invariant that matters most here.

**Deliberately checked**: an order line with a blank or unrecognised set must still be visible,
in an explicitly labelled group, sorted last within its game. Not dropped.

## Story 2 — Pick one card at a time on a phone

Use a phone-sized viewport (Playwright project, or dev tools device emulation).

1. Open a claimed order. The focused view appears, and there is no way to reach the whole-order
   view from here (FR-009). Resize to desktop width and confirm the reverse: the whole order,
   with no way to the card view.
2. Confirm the way out is always present — the dashboard link is reachable from every card, not
   only at the end of the order (FR-030).
3. **The central check**: navigate forward and back across several products without confirming
   anything. Return to the first product. Its outcome must be unchanged, and no pick request
   must have been issued. Watch the network panel — "looks unchanged" is not the assertion.
4. Confirm a pick explicitly. Only that product changes, and the change is visible on the
   button itself — not merely recorded.
5. Navigate using on-screen controls alone, never swiping. Every product must be reachable.
   Then confirm swiping moves between cards and never records anything.
6. **Leave a product unresolved and cross into the next box.** Nothing may block or interrupt
   the move (FR-019a). The new box is announced on the card itself, naming its size.
7. Check the box band throughout: the set name, the position within the box ("2 of 3"), and a
   fill that tracks that position. It shows where the picker is standing, not what has been
   pulled.
8. Continue past the last card. The final review must appear (FR-031), listing every product
   with its quantity and outcome, without images, headed by the number of physical cards that
   should be in hand. Confirm a product left unresolved in step 6 is listed as not looked at.
9. Jump back to a listed product from the review, resolve it, and return. The count updates.
10. Finish from the review. The order is released and the dashboard offers to claim it again.

**Passes when** nothing but the explicit record action ever records an outcome, nothing ever
blocks the picker from moving, and no unresolved product can reach the end unlisted.

**Viewing an order nobody holds**: open an unclaimed order on a phone and walk it to the review.
Claim is the primary action throughout, no picking action is offered, and the review's button
closes the screen rather than offering to finish picking — it must not attempt to release a
claim this employee never held.

## Story 3 — Start an order from the order itself

1. As a picker holding no order, open an unclaimed order. It must **not** become claimed —
   verify from a second session that it is still available.
2. Claim it from that screen. Picking actions become available.
3. **Concurrency**: from two sessions, claim the same order at the same moment. Exactly one
   succeeds; the other is told who holds it. (The rule itself is feature 013's and unchanged —
   this confirms the new surface reports it correctly.)
4. Return to the dashboard while holding the order. It must offer to **resume** that order
   rather than to start another.
5. Open a *different* available order while holding one. The application explains you already
   hold an order instead of showing a claim action that would fail.
6. **The case worth doing deliberately**: report an issue on your held order so it moves to
   Needs Attention, then return to the dashboard. It must *still* offer to resume — the claim
   is retained, and a resume affordance that only scans In Progress would fail here.

**Passes when** viewing is always safe, claiming is explicit, and a picker holding an order is
never offered an action that fails.

## Before calling it done

Per `CLAUDE.md`'s Definition of Done, beyond the suites above:

- Tests were written Red → Green → Refactor, not added afterwards to cover finished code
- `dotnet csharpier format .` and the frontend formatter are clean
- Zero new build warnings
- No secrets, no plaintext PINs, no customer PII in any new payload or log
- Quantity greater than one is still visually emphasised in the focused view — PRD §5.3 and
  §15 apply there exactly as they do in the list, and the focused view is where a missed
  "PULL 3 COPIES" costs the most
- `/branch-review` run with zero remaining Required findings, then `/speckit-converge`
