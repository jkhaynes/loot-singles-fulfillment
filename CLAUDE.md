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

Spec Kit owns planning:

- Constitution
- Feature specification
- Clarification
- Technical planning
- Task breakdown

## Superpowers

Superpowers owns execution, after Spec Kit planning is approved:

- Test-driven development
- Executing approved tasks
- Systematic debugging
- Verification before completion
- Specification compliance review
- Code quality review
- Finishing development branches

## Important Boundary

**Spec Kit plans the work. Superpowers executes and verifies the work.**

Do not use Superpowers brainstorming/planning skills to originate or replace a product specification or technical plan when approved Spec Kit artifacts already exist for that feature. If Superpowers execution discovers a contradictory requirement, a missing business rule, an unclear workflow, an architectural conflict, or a requirement that cannot reasonably be implemented as specified, it must **stop and surface the problem** rather than silently redesigning the feature. Return to Spec Kit clarification/planning to resolve it.

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

A feature is not complete merely because code runs. Before considering work complete, confirm:

- Required automated tests pass
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
