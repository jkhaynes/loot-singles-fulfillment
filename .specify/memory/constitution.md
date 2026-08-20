<!--
Sync Impact Report
Version change: 1.0.1 → 2.0.0 (MAJOR — a governance section removed/redefined, and Principle IV
redefined from "test-first for critical rules only" to universal, non-negotiable TDD)
Modified principles:
  - IV. Test-First Where Appropriate (NON-NEGOTIABLE for Critical Rules) → IV. Test-Driven
    Development (NON-NEGOTIABLE). No longer scoped to a named list of "high-risk behaviors" —
    the full Red → Green → Refactor cycle is now required for all new or modified application
    behavior, with explicit task-generation, implementation, exception, and verification rules.
Modified sections:
  - "Spec Kit / Superpowers Boundary" → "Spec Kit Ownership": Superpowers is no longer part of
    this project's workflow. Spec Kit now owns the complete feature lifecycle end to end,
    including implementation and convergence verification, not just planning through task
    breakdown. The "stop and surface conflicts rather than silently redesign" governance rule
    is preserved, reattributed to Spec Kit's own implement/analyze/converge phases.
  - Development Workflow: pipeline updated to specification → clarification → technical plan →
    checklist (when useful) → task breakdown → analyze → human approval → feature branch/
    worktree → Spec Kit implementation (strict TDD per Principle IV) → convergence verification
    → pull request → CI → human review → merge, with a repeat-until-converged loop. Replaces
    "Superpowers TDD execution" with Spec-Kit-native implementation and convergence.
  Rationale for both: explicit Product Owner/developer decision to migrate off Superpowers to a
  Spec-Kit-only workflow with TDD enforced directly through this constitution rather than a
  second methodology layered on top of Spec Kit. This is a workflow/tooling change, not a
  product requirement change — no product functionality was altered.
Added sections: none
Removed sections: none (the former "Spec Kit / Superpowers Boundary" section was renamed and
  rewritten in place, not deleted outright — Modified above)
Follow-up TODOs: none
-->

# Loot Singles Fulfillment Constitution

## Core Principles

### I. Product Owner Authority
Confirmed Product Owner decisions outrank assumptions and outrank every other artifact in this repository, including this constitution. The source-of-truth hierarchy, highest first, is: Product Owner decisions → approved PRD → approved Spec Kit feature specification → approved Spec Kit technical plan → approved task breakdown → implementation. When artifacts at different levels conflict, work MUST stop for clarification rather than silently resolving in favor of the lower-level artifact.

### II. No Invented Requirements
Product functionality MUST NOT be added because it seems useful, standard, or convenient. Every requirement MUST trace to a confirmed Product Owner decision, the approved PRD (`docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md`), or an approved Spec Kit feature specification. Open questions documented in the PRD remain open; they MUST NOT be silently converted into implementation assumptions.

### III. Small, Reviewable Changes
No product feature work is committed directly to `main`. Each feature is developed on its own branch (or worktree), scoped to one approved task breakdown, and merged only via reviewed pull request after CI passes.

### IV. Test-Driven Development (NON-NEGOTIABLE)
For new or modified application behavior, the following Red → Green → Refactor cycle is required, not optional or "for critical rules only":

1. Identify the behavior or acceptance criterion being implemented.
2. Write the smallest meaningful automated test that demonstrates that behavior.
3. Run the test and confirm it fails for the expected reason.
4. Implement only enough production code to make that test pass.
5. Run the relevant test and confirm it passes.
6. Refactor while keeping the test suite green.
7. Continue with the next behavior.

The planned stack is xUnit (backend), Vitest and React Testing Library (frontend), and Playwright (critical end-to-end flows).

**Task generation**: Spec Kit task breakdowns MUST include applicable automated test tasks. Where a behavior requires a test, the test task MUST appear before its corresponding implementation task. Tests MUST trace to user stories, acceptance criteria, functional requirements, business rules, and important error/edge cases.

**Implementation**: New feature behavior MUST NOT be implemented first with tests added afterward merely to satisfy coverage. Existing tests MUST remain green throughout implementation.

**Exceptions**: TDD does not require artificial tests for work where executable behavioral testing is not meaningful — documentation-only changes, certain static configuration changes, repository metadata. Any exception applied to application behavior MUST be stated explicitly (in the plan or task breakdown), never a silent bypass.

**Verification**: A feature is not complete merely because production code exists. Completion requires: required automated tests exist; required tests pass; existing tests pass; implementation satisfies the approved specification; applicable acceptance criteria have verification evidence. This constitution is the sole, authoritative TDD policy for this project — no separate TDD tool or extension governs it.

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

## Spec Kit Ownership

Spec Kit is this project's sole AI-assisted feature development methodology. It owns the complete structured feature lifecycle: constitution, feature specification, clarification, technical planning, task breakdown, artifact analysis, implementation, and convergence verification. No second planning or implementation methodology is layered on top of Spec Kit. If implementation surfaces a contradictory requirement, a missing business rule, an unclear workflow, an architectural conflict, or a requirement that cannot reasonably be implemented as specified, work MUST stop and return to Spec Kit clarification/planning — the specification and plan are never silently redesigned, and higher-level artifacts are never normalized to match whatever was implemented.

## Development Workflow

Requirement → Spec Kit specification → clarification → technical plan → checklist (when useful) → task breakdown → analyze → human approval → feature branch/worktree → Spec Kit implementation (strict Red → Green → Refactor TDD per Principle IV) → convergence verification → pull request → CI → human review → merge. If convergence surfaces remaining required work, implementation and convergence repeat until converged. The Spec Kit task breakdown (`tasks.md`) is the tracker for in-progress work; no GitHub issue is required per feature. See `docs/development/ai-assisted-development-workflow.md` for the full workflow and project-specific validation gates.

## Governance

This constitution is subordinate to confirmed Product Owner decisions and the approved PRD, per the source-of-truth hierarchy in `CLAUDE.md`. All specifications, plans, and pull requests must be consistent with these principles; deviations must be explicitly justified and, where they affect product behavior, escalated to the Product Owner rather than decided unilaterally during implementation. Amendments to this constitution require documentation of the change and rationale, and must not lower a safety-related principle (Sections V, VI, VII) without explicit Product Owner approval.

**Version**: 2.0.0 | **Ratified**: 2026-08-19 | **Last Amended**: 2026-08-20
