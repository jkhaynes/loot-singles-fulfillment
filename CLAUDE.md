# CLAUDE.md

This file must be usable by Claude Code without requiring knowledge of any prior conversation. It defines the rules of engagement for AI-assisted work on this repository.

## Project

**Loot Singles Fulfillment** is an internal fulfillment application for Loot Card Shop. V1 replaces the printed TCGplayer invoice for the **picking** portion of order fulfillment with a responsive, set-aware, visual picking experience that strongly emphasizes high-risk information (quantity greater than one, variant, set, card identity), supports multiple concurrent pickers with exclusive order claiming, and represents picking problems explicitly instead of forcing a false happy path. See [`docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md`](docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md) for full detail.

## Product Authority

Source-of-truth hierarchy, highest first:

1. Confirmed Product Owner decisions
2. Approved PRD
3. Approved Spec Kit feature specification
4. Approved Spec Kit technical plan
5. Approved task breakdown
6. Implementation

If artifacts at different levels conflict, **stop and clarify** rather than silently resolving the conflict by favoring the lower-level (more implementation-adjacent) artifact.

## No Invented Requirements

Do not add product functionality because it seems useful, standard, or convenient. Every requirement must trace to one of:

- A confirmed Product Owner decision
- The approved PRD
- An approved Spec Kit feature specification

Open questions in the PRD (Sections 41–42) are open. Do not silently convert them into implementation assumptions.

## Spec Kit

Spec Kit is this project's sole AI-assisted feature development methodology. It owns the complete feature lifecycle, end to end:

- Constitution
- Feature specification
- Clarification
- Technical planning
- Human architecture and changeability review
- Task breakdown
- Artifact analysis (`/speckit-analyze`)
- Implementation (`/speckit-implement`)
- Code and Design Review (`/code-design-review`)
- Convergence verification (`/speckit-converge`)

No second planning or implementation methodology is layered on top of Spec Kit. Strict Test-Driven Development (Red → Green → Refactor) is required for all new or modified application behavior, per the project constitution's Principle IV — this is enforced through the constitution itself, not a separate tool or extension. `/code-design-review` is part of this same Spec Kit lifecycle, not a second methodology — see "Code and Design Review Gate" below for how it fits between implementation and convergence.

## Spec Kit Git Auto-Commit Confirmation

The installed Spec Kit `git` extension checkpoints work by committing after `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, and `/speckit-tasks`. These hooks are configured `optional: true` specifically so they are confirmed rather than run silently or skipped. When one of these hook blocks appears (`**Optional Hook**: git ... Prompt: <question>`), do not silently skip it and do not auto-run it: show a brief summary of what changed (files touched, one-line description) and ask the stated prompt question directly. Only run the commit (`speckit-git-commit`) after the user gives explicit confirmation.

## Important Boundary

**Spec Kit plans, implements, and verifies the work — there is no second methodology layered on top of it.**

Do not originate or replace a product specification or technical plan by improvising when approved Spec Kit artifacts already exist for that feature. If implementation discovers a contradictory requirement, a missing business rule, an unclear workflow, an architectural conflict, or a requirement that cannot reasonably be implemented as specified, **stop and surface the problem** rather than silently redesigning the feature or normalizing the specification to match whatever was implemented. Return to Spec Kit clarification/planning to resolve it.

This applies equally to findings from `/code-design-review`: a finding rooted in a flawed technical plan returns to `/speckit-plan`; a finding rooted in an unresolved or contradictory requirement returns to `/speckit-clarify`. Ordinary implementation-level findings are captured as new tasks in the existing `tasks.md`, not a separate tracker — see "Code and Design Review Gate" below.

## Code and Design Review Gate

Before a feature's application-code changes proceed to `/speckit-converge`, they MUST pass `/code-design-review` — Spec Kit's post-implementation review gate.

- **Human architecture/changeability review** (after `/speckit-plan`) evaluates the *proposed design* before any implementation exists.
- **`/code-design-review`** (after `/speckit-implement`, once local build/tests pass) evaluates the *actual implementation* against the approved spec, plan, and constitution.
- **`/speckit-converge`** verifies the finished feature against the approved artifacts once review is clean; it does not re-review code quality.

`/code-design-review` classifies findings as **Must Fix** or **Advisory**:

- Must Fix findings are captured as new tasks in the feature's existing `tasks.md` (never a separate or competing task list) and resolved through `/speckit-implement`, then `/code-design-review` is re-run — repeat until no Must Fix findings remain.
- Advisory findings do not block completion.
- A Must Fix finding that traces to a flawed technical plan means returning to `/speckit-plan`, not patching around it in code.
- A Must Fix finding that traces to an unresolved or contradictory requirement means returning to `/speckit-clarify`.

This gate is required for application-code changes before `/speckit-converge`. It does not apply to documentation-only or configuration-only changes.

## Product-Specific Safety Rules

- Quantity greater than one is a high-risk requirement and must receive strong visual emphasis wherever it is displayed.
- Exclusive order claiming must be concurrency-safe and enforced server-side; two pickers must never be able to claim the same order.
- Picking issues must not be silently ignored or dropped.
- An order with an unresolved blocking picking issue must not be represented as successfully picked.
- TCGplayer imported order data is authoritative.
- Catalog enrichment is supplemental and must never silently overwrite authoritative order attributes.
- **No image is better than the wrong image.** An ambiguous or unconfident catalog/image match must never be silently displayed as a best guess.
- PDF import/parsing must fail safely — reject and surface the problem rather than silently create an incomplete or incorrect order.
- Employee PINs must never be stored in plaintext.
- Customer PII should be minimized and must not be exposed to pickers beyond what picking requires.
- Do not add multi-tenant SaaS complexity; this is single-business software.
- Do not implement future V2/V3 features (packing verification, barcode/QR handoff, batch picking, camera-assisted verification) during V1 unless explicitly approved by the Product Owner.

## Definition of Done

A feature is not complete merely because code runs, or because production code exists. Before considering work complete, confirm:

- Required automated tests exist, following Red → Green → Refactor (constitution Principle IV) — not written after the fact merely to satisfy coverage
- Required tests pass
- Existing tests pass
- Implementation matches the approved specification
- Relevant error cases are handled
- Concurrency behavior is tested where applicable
- No secrets are committed
- No plaintext PINs exist
- Card image ambiguity fails safely (no image, not a guess)
- Parser changes are validated against representative fixtures
- Playwright validation is performed for critical user flows
- Documentation is updated where required
- Code review is complete
- `/code-design-review` has been run against the implementation with zero remaining Must Fix findings (Advisory findings do not block) — required before `/speckit-converge`
- `/speckit-converge` ultimately reports convergence for the feature
