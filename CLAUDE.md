# CLAUDE.md

This file must be usable by Claude Code without requiring knowledge of any prior conversation. It defines the rules of engagement for AI-assisted work on this repository.

## Project

**Loot Singles Fulfillment** is an internal fulfillment application for Loot Card Shop. V1 replaces the printed TCGplayer invoice for the **picking** portion of order fulfillment with a responsive, set-aware, visual picking experience that strongly emphasizes high-risk information (quantity greater than one, variant, set, card identity), supports multiple concurrent pickers with exclusive order claiming, and represents picking problems explicitly instead of forcing a false happy path. See [`docs/prd/Loot_Singles_Fulfillment_PRD_v0.7.md`](docs/prd/Loot_Singles_Fulfillment_PRD_v0.7.md) for full detail.

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

## Workflow

This project uses **Spec Kit for design** and **Superpowers for implementation**. Strict Test-Driven Development (Red → Green → Refactor) applies to all new or modified application behavior on every track (constitution Principle IV).

**1. Pick the track by size of change**

- **Feature** (new behavior, touches several files, worth remembering why): the full track below.
- **Small change** (bug fix, tweak, refactor with no new user-visible behavior): skip Spec Kit. Use Superpowers directly: systematic-debugging for bugs, test-driven-development for everything else. Anything that adds user-visible behavior is a feature and needs a spec.
- **Spike / experiment**: Superpowers brainstorming, throwaway branch, no spec. If it survives, promote it to a feature.

**2. Feature track: design (Spec Kit owns this)**

1. `/speckit-specify`: what and why, no tech choices
2. `/speckit-clarify`: resolve ambiguity before planning
3. `/speckit-plan`: technical approach
4. Human architecture and changeability review of the plan (constitution, "Architecture and Changeability Review")
5. `/speckit-tasks`: ordered task list, tests before the code they cover
6. `/speckit-analyze`: consistency check across spec, plan and tasks

During these steps Spec Kit is the source of truth. Do not run Superpowers brainstorming or writing-plans on top of a Spec Kit feature; that creates a second, competing plan. `tasks.md` is the plan Superpowers carries out.

**3. Feature track: implementation (Superpowers owns this)**

Do NOT use `/speckit-implement`, `/branch-review`, `/review-remediation` or `/speckit-converge`. Instead, for `specs/NNN-*/tasks.md`:

1. Work on the feature branch in the main checkout, so the owner can follow along in their own editor. Do not use git worktrees (skip using-git-worktrees).
2. subagent-driven-development: one fresh subagent per task from `tasks.md`, test-first (red, green, refactor), then a spec-compliance review and a code-quality review. Check each task off in `tasks.md` as it lands.
3. requesting-code-review: before merge. Fix Critical and Important findings test-first (the regression test that shows the defect comes before the fix). A review round with no Critical or Important findings is the stopping point; further rounds have diminishing returns.
4. finishing-a-development-branch (or `/ship`): merge or PR. A merge to `main` deploys to stage.

If implementation or review shows the spec or plan is wrong, **stop and surface it**: a flawed plan returns to `/speckit-plan`, and an unresolved or contradictory requirement returns to `/speckit-clarify`. Update `spec.md` or `plan.md` first, then continue. Never silently redesign the feature or normalize the spec to match the code.

## Spec Kit Git Auto-Commit Confirmation

The installed Spec Kit `git` extension checkpoints work by committing after `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, and `/speckit-tasks`. These hooks are configured `optional: true` specifically so they are confirmed rather than run silently or skipped. When one of these hook blocks appears (`**Optional Hook**: git ... Prompt: <question>`), do not silently skip it and do not auto-run it: show a brief summary of what changed (files touched, one-line description) and ask the stated prompt question directly. Only run the commit (`speckit-git-commit`) after the user gives explicit confirmation.

## Important Boundary

Do not originate or replace a product specification or technical plan by improvising when approved Spec Kit artifacts already exist for that feature. If implementation discovers a contradictory requirement, a missing business rule, an unclear workflow, an architectural conflict, or a requirement that cannot reasonably be implemented as specified, **stop and surface the problem** rather than silently redesigning the feature or normalizing the specification to match whatever was implemented. Return to Spec Kit clarification/planning to resolve it.

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
- The one exception is the stored per-order packing slip (PRD §27): packing needs the customer's address, so the application keeps one slip per order. It is reachable only from the packing workflow, never from a picking surface, and every retrieval is logged. Do not widen that access, and do not extract further customer fields out of a slip into the data model.
- Do not add multi-tenant SaaS complexity; this is single-business software.
- Do not implement future V2/V3 features (packing verification, barcode/QR handoff, batch picking, camera-assisted verification) during V1 unless explicitly approved by the Product Owner.

## TCGplayer API Agreement

Loot Investments LLC's access to the TCGplayer API is governed by the TCGplayer API Terms and Conditions plus a signed **Legacy Qualified Addendum** (executed 2026-09-21). TCGplayer issued Loot's API keys under this addendum in 2026. Loot had no earlier API connection. Breaking the terms can end API access immediately. Every spec, plan, task, test and debugging step that touches the TCGplayer API must follow these rules:

- **One set of credentials.** Use the keys TCGplayer issued to Loot, and the store access token from authorizing those keys for Loot's own store, a one-time step done by a person through TCGplayer's Store Authorization Workflow. Don't create additional keys or integrations. The application itself never runs the authorization flow; it only reads the resulting token from configuration.
- **No more than 300 API calls per minute.** The application must throttle or queue its own calls to stay under the limit.
- **Identify every request.** Each request carries a User-Agent naming the business (Loot Investments LLC) and the application name and version.
- **Internal business use only.** API data is used solely for Loot's in-house fulfillment. It must not be republished, resold, transferred, licensed or exposed to any third party, including by sending it to external catalog or image services. Card images for API-imported lines come from TCGplayer's own data (Product Owner decision, 2026-10-08).
- **No API data in third-party generative AI tools.** This includes AI coding assistants such as Claude Code. Agents must not make live TCGplayer API calls and read the responses, and must not ask for real responses. Test fixtures are synthetic, built from TCGplayer's published response schema. When live behavior needs investigating, a human inspects it and describes it without pasting API data.
- **Credentials are secrets.** API keys and tokens live only in deployment configuration and user secrets, are never committed, and are never shared with a third party or pasted into an AI tool. The TCGplayer account itself is operated by Loot and is never shared.
- **Channels.** API data may be used only for Loot's physical store, its own webstore, TCGplayer.com and eBay.com.

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
- New or materially changed application behavior has been evaluated for whether it warrants production logging (per the constitution's Observability standard); important events and failures are logged via `ILogger<T>` with safe, non-PII structured fields where warranted — logging is not added reflexively for behavior that doesn't need it
- Documentation is updated where required
- Every task in the feature's `tasks.md` is checked off, and each passed subagent-driven-development's spec-compliance and code-quality reviews
- requesting-code-review has been run on the branch with no remaining Critical or Important findings
