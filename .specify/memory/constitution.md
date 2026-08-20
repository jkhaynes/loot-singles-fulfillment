# Loot Singles Fulfillment Constitution

## Core Principles

### I. Product Owner Authority
Confirmed Product Owner decisions outrank assumptions and outrank every other artifact in this repository, including this constitution. The source-of-truth hierarchy, highest first, is: Product Owner decisions → approved PRD → approved Spec Kit feature specification → approved Spec Kit technical plan → approved task breakdown → implementation. When artifacts at different levels conflict, work MUST stop for clarification rather than silently resolving in favor of the lower-level artifact.

### II. No Invented Requirements
Product functionality MUST NOT be added because it seems useful, standard, or convenient. Every requirement MUST trace to a confirmed Product Owner decision, the approved PRD (`docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md`), or an approved Spec Kit feature specification. Open questions documented in the PRD remain open; they MUST NOT be silently converted into implementation assumptions.

### III. Small, Reviewable Changes
No product feature work is committed directly to `main`. Each feature is developed on its own branch (or worktree), scoped to one approved task breakdown, and merged only via reviewed pull request after CI passes.

### IV. Test-First Where Appropriate (NON-NEGOTIABLE for Critical Rules)
The planned stack is xUnit (backend), Vitest and React Testing Library (frontend), and Playwright (critical end-to-end flows). High-risk behaviors — exclusive order claiming under concurrency, quantity-greater-than-one preservation through import/persistence/API/UI, unresolved-issue blocking of pick completion, ambiguous-match image suppression, and safe parser failure — REQUIRE automated test coverage before being considered done.

### V. Safe Failure Over Silent Corruption
The system MUST favor rejecting questionable data over silently accepting it. TCGplayer packing-slip parsing MUST fail safely and surface an import problem rather than silently create an incomplete or incorrect order. Catalog enrichment MUST NOT silently overwrite authoritative order attributes. An ambiguous or insufficiently confident catalog/image match MUST NOT be displayed as a best guess — **no image is better than the wrong image**.

### VI. Server-Enforced Critical Business Rules
The backend, not the frontend, is the enforcement point for authentication, exclusive order claiming/concurrency, and order state transitions. Exclusive order claiming MUST be concurrency-safe: two employees pressing "Pick Next Order" (or claiming the same order) at approximately the same time MUST NOT both succeed in claiming the same order.

### VII. Data Minimization and Credential Security
Customer PII MUST be minimized and MUST NOT be exposed to pickers beyond what picking requires. Employee PINs MUST NEVER be stored in plaintext; secure hashing, failed-attempt protection, and session management are required. No credentials, PINs, connection strings, API keys, Azure credentials, or tokens may ever be committed to this repository.

### VIII. One Responsive Product
Desktop and mobile phone are served by a single responsive Progressive Web App, not separate codebases. Responsive UX differences MUST NOT create separate fulfillment systems.

### IX. Replaceable Integrations
TCGplayer order ingestion (currently packing-slip PDF parsing) and card catalog enrichment (currently Scryfall, Pokémon TCG API, Lorcast, and a still-undetermined One Piece provider) are adapters, not the application domain. Either MUST be replaceable — by a future supported TCGplayer API, or by different/additional catalog providers — without redesigning the picking domain or user experience. Catalog providers are supplemental and MUST NOT become the source of truth for sold order data; imported TCGplayer order data remains authoritative.

### X. Single-Business Scope
This is single-business software for Loot Card Shop. Multi-tenant SaaS complexity MUST NOT be introduced for hypothetical future customers.

### XI. Reliability During Fulfillment
Low and predictable infrastructure cost is preferred, but cost optimization MUST NOT knowingly make the production picking workflow unreliable. Reliability during active fulfillment work takes priority over marginal cost savings.

## Spec Kit / Superpowers Boundary

Spec Kit owns planning: constitution, feature specification, clarification, technical planning, and task breakdown. Superpowers owns execution after planning is approved: test-driven development, executing approved tasks, systematic debugging, verification, and code quality review. Superpowers MUST NOT originate or silently redesign a feature; if execution surfaces a contradictory requirement, missing business rule, unclear workflow, or architectural conflict, it MUST stop and return the feature to Spec Kit clarification/planning.

## Development Workflow

Requirement → Spec Kit specification → clarification → technical plan → task breakdown → human approval → GitHub issue → feature branch/worktree → Superpowers TDD execution → verification against the approved specification → code quality review → pull request → CI → human review → merge. See `docs/development/ai-assisted-development-workflow.md` for the full workflow and project-specific validation gates.

## Governance

This constitution is subordinate to confirmed Product Owner decisions and the approved PRD, per the source-of-truth hierarchy in `CLAUDE.md`. All specifications, plans, and pull requests must be consistent with these principles; deviations must be explicitly justified and, where they affect product behavior, escalated to the Product Owner rather than decided unilaterally during implementation. Amendments to this constitution require documentation of the change and rationale, and must not lower a safety-related principle (Sections V, VI, VII) without explicit Product Owner approval.

**Version**: 1.0.0 | **Ratified**: 2026-08-19 | **Last Amended**: 2026-08-19
