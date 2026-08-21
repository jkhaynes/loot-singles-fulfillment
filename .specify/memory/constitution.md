<!--
Sync Impact Report
Version change: 3.1.0 → 3.2.0
MINOR — added a new, mandatory engineering standards section governing Entity Framework Core
querying, tracking, persistence, relationship loading, bulk operations, cancellation, and
performance review. No Core Principle (I-XIII) was added, removed, redefined, or weakened.

Added sections:
  - Entity Framework Core Engineering Standards
    Establishes testable rules for async database I/O, synchronous change-tracker additions,
    no-tracking reads, projection, server-side query execution, existence checks, pagination,
    intentional relationship loading, DbContext lifetime and threading, unit-of-work saves,
    set-based bulk operations, cancellation propagation, and generated-query review.

Modified principles:
  None.

Removed sections:
  None.

Rationale:
The constitution had general persistence and efficiency guidance but no explicit EF Core standard.
This amendment makes the intended database-access practices reviewable and prevents mechanical use
of asynchronous APIs, unnecessary tracking or materialization, avoidable round trips, and unsafe
DbContext usage.

Follow-up TODOs: None.
-->

# Loot Singles Fulfillment Constitution

## Core Principles

### I. Product Owner Authority

Confirmed Product Owner decisions are authoritative for product behavior, business workflow, priorities, and acceptance criteria.

For product behavior and scope, the source-of-truth hierarchy, highest first, is:

Product Owner decisions → approved PRD → approved Spec Kit feature specification → approved Spec Kit technical plan → approved task breakdown → implementation.

The Product Owner does not implicitly override engineering, security, privacy, testing, reliability, or architectural principles established by this constitution. If a Product Owner decision conflicts with one of those principles, the conflict MUST be surfaced for explicit resolution rather than silently weakening the engineering standard or silently changing the product requirement.

When artifacts at different levels conflict, work MUST stop for clarification rather than silently resolving in favor of the lower-level artifact.

### II. No Invented Requirements

Product functionality MUST NOT be added because it seems useful, standard, or convenient. Every requirement MUST trace to a confirmed Product Owner decision, the approved PRD (`docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md`), or an approved Spec Kit feature specification.

Open questions documented in the PRD remain open; they MUST NOT be silently converted into implementation assumptions.

Engineering abstractions, extensibility mechanisms, validation behavior, or infrastructure decisions MUST NOT introduce new user-facing product behavior that has not been approved.

### III. Small, Reviewable Changes

No product feature work is committed directly to `main`.

Each feature is developed on its own branch or worktree, scoped to one approved task breakdown, and merged only through a reviewed pull request after CI passes.

Changes SHOULD be kept small enough that their behavior, architectural impact, and test evidence can be meaningfully reviewed.

Unrelated refactoring MUST NOT be bundled into a feature merely because nearby code could also be improved. When a required architectural correction is necessary to implement the approved feature safely or maintainably, it MUST be represented in the plan and task breakdown.

### IV. Test-Driven Development (NON-NEGOTIABLE)

For new or modified application behavior, the following Red → Green → Refactor cycle is required, not optional or limited only to critical rules:

1. Identify the behavior or acceptance criterion being implemented.
2. Write the smallest meaningful automated test that demonstrates that behavior.
3. Run the test and confirm it fails for the expected reason.
4. Implement only enough production code to make that test pass.
5. Run the relevant test and confirm it passes.
6. Refactor while keeping the test suite green.
7. Continue with the next behavior.

The planned stack is xUnit for backend testing, Vitest and React Testing Library for frontend testing, and Playwright for critical end-to-end flows.

**Task generation:** Spec Kit task breakdowns MUST include applicable automated test tasks. Where a behavior requires a test, the test task MUST appear before its corresponding implementation task. Tests MUST trace to user stories, acceptance criteria, functional requirements, business rules, and important error and edge cases.

**Implementation:** New feature behavior MUST NOT be implemented first with tests added afterward merely to satisfy coverage. Previously passing tests MUST remain green throughout implementation. During the Red phase, only the newly introduced test or tests demonstrating the behavior currently being implemented are expected to fail, and they MUST fail for the expected reason.

**Refactoring:** Refactoring performed during the TDD cycle MUST preserve externally observable behavior unless a behavior change is itself part of the approved requirement. The relevant test suite MUST remain green throughout refactoring.

**Exceptions:** TDD does not require artificial tests for work where executable behavioral testing is not meaningful, including documentation-only changes, certain static configuration changes, and repository metadata. Any exception applied to application behavior MUST be stated explicitly in the plan or task breakdown and MUST NOT be a silent bypass.

**Verification:** A feature is not complete merely because production code exists. Completion requires that required automated tests exist, required tests pass, existing tests pass, implementation satisfies the approved specification, and applicable acceptance criteria have verification evidence.

This constitution is the sole authoritative TDD policy for this project. No separate TDD methodology, tool, or extension governs feature implementation.

### V. Safe Failure Over Silent Corruption

The system MUST favor rejecting questionable data over silently accepting it.

TCGplayer packing-slip parsing MUST fail safely and surface an import problem rather than silently create an incomplete or incorrect order.

Catalog enrichment MUST NOT silently overwrite authoritative order attributes.

An ambiguous or insufficiently confident catalog or image match MUST NOT be displayed as a best guess.

**No image is better than the wrong image.**

Expected malformed external input SHOULD be modeled as an application condition rather than treated as an unexpected programming failure.

### VI. Server-Enforced Critical Business Rules

The backend, not the frontend, is the enforcement point for authentication, exclusive order claiming and concurrency, and order state transitions.

Exclusive order claiming MUST be concurrency-safe: two employees pressing "Pick Next Order" or attempting to claim the same order at approximately the same time MUST NOT both succeed in claiming the same order.

Frontend validation MAY improve usability but MUST NOT be the sole enforcement mechanism for critical business rules.

### VII. Data Minimization and Credential Security

Customer PII MUST be minimized and MUST NOT be exposed to pickers beyond what picking requires.

Employee PINs MUST NEVER be stored in plaintext. Secure hashing, failed-attempt protection, and session management are required.

No credentials, PINs, connection strings, API keys, Azure credentials, tokens, or other secrets may ever be committed to this repository.

Sensitive information MUST NOT be added to logs, fixtures, example files, screenshots, test data, or documentation when synthetic or sanitized data can serve the same purpose.

### VIII. One Responsive Product

Desktop and mobile phone are served by a single responsive Progressive Web App, not separate codebases.

Responsive UX differences MUST NOT create separate fulfillment systems or duplicate business logic.

Platform-specific presentation differences MAY exist where needed for usability, but the same domain and application behavior MUST remain authoritative across form factors.

### IX. Replaceable Integrations

TCGplayer order ingestion, currently packing-slip PDF parsing, and card catalog enrichment, currently Scryfall, Pokémon TCG API, Lorcast, and a still-undetermined One Piece provider, are adapters rather than the application domain.

Either MUST be replaceable by a future supported TCGplayer API or by different or additional catalog providers without redesigning the picking domain or user experience.

Catalog providers are supplemental and MUST NOT become the source of truth for sold order data. Imported TCGplayer order data remains authoritative.

Source-specific import adapters MUST translate external representations into source-neutral application input before source-independent business validation where practical.

Expected malformed external input MUST be represented through typed results, explicit failure concepts, or another stable programmatic contract. Consumers MUST NOT determine application behavior by inspecting human-readable exception message text.

Source-independent validation and business rules MUST NOT be duplicated across source-specific adapters.

Adding another supported ingestion source MUST NOT require rewriting the picking domain or duplicating source-independent validation behavior.

Provider-specific quirks MUST remain contained within the appropriate adapter boundary rather than leaking throughout the application.

### X. Single-Business Scope

This is single-business software for Loot Card Shop.

Multi-tenant SaaS complexity MUST NOT be introduced for hypothetical future customers.

Extensibility requirements in this constitution apply to real architectural variation within the approved product scope. They MUST NOT be interpreted as a requirement to generalize the application for unknown future businesses or customers.

### XI. Reliability During Fulfillment

Low and predictable infrastructure cost is preferred, but cost optimization MUST NOT knowingly make the production picking workflow unreliable.

Reliability during active fulfillment work takes priority over marginal cost savings.

Failure modes that affect fulfillment MUST be explicit and observable. The application MUST NOT report an operation as successful when required persistence, validation, concurrency enforcement, or authoritative processing has failed.

### XII. Maintainable and Extensible Design

Application code MUST be designed for maintainability and for foreseeable variation already implied by approved requirements.

Extensibility does not mean supporting every hypothetical future use. It means avoiding designs that make known or reasonably foreseeable changes unnecessarily invasive.

The following rules apply:

- Each component MUST have a clear, focused responsibility.
- Parsing, validation, domain behavior, persistence, infrastructure, presentation, and orchestration MUST remain separate concerns unless the technical plan documents a concrete reason to combine them.
- Domain and application logic MUST NOT depend directly on UI frameworks, persistence implementations, external service implementations, or source-specific import formats.
- Expected validation, parsing, and business failures MUST be represented through typed results, domain concepts, or other explicit programmatic contracts. Production control flow MUST NOT depend on parsing human-readable exception message text.
- Domain objects SHOULD make invalid states difficult to represent.
- Untrusted, incomplete, or partially parsed external data SHOULD use an appropriate input, candidate, parsed, or result model rather than prematurely constructing a domain object that implies validity.
- Where approved requirements reveal a genuine variation point, the design MUST provide an appropriate extension boundary rather than requiring repeated modification of unrelated orchestration logic or continued growth of a central conditional method.
- Shared domain, business, and validation rules MUST NOT be duplicated across adapters, controllers, UI components, persistence code, or other source-specific implementations.
- Public APIs and abstractions MUST remain focused and minimal.
- Composition SHOULD be preferred over inheritance unless inheritance accurately represents the domain relationship and provides a concrete benefit.
- Dependencies SHOULD point toward stable domain and application concepts rather than outward toward volatile infrastructure details.
- Infrastructure-specific concerns MUST remain at appropriate application boundaries.
- A component that coordinates multiple collaborators SHOULD primarily orchestrate them rather than absorb their individual parsing, validation, persistence, or business responsibilities.
- Adding the next foreseeable implementation of a known variation point SHOULD primarily require adding or replacing the relevant implementation rather than modifying several unrelated consumers.

SOLID principles are design guidance under this principle, not a requirement to create an interface, abstraction, or design pattern for every class.

During technical planning, significant components MUST be evaluated for responsibility, dependency direction, expected variation, and change cost before implementation tasks are generated.

If adding the next already-foreseeable case would require modifying several unrelated components, duplicating domain rules, interpreting exception text, or substantially expanding central branching logic, the plan MUST consider a more appropriate boundary or extension mechanism before implementation.

### XIII. Simplicity and Proportional Abstraction

Maintainability and extensibility MUST NOT be pursued through speculative abstraction or unnecessary architecture.

The implementation MUST prefer the simplest design that satisfies:

- The approved requirements
- The technical plan
- This constitution
- Known or reasonably foreseeable change points

Interfaces, factories, strategies, repositories, pipelines, base classes, providers, handlers, builders, and other abstractions MUST have a concrete purpose such as:

- Supporting a known substitution or variation point
- Isolating an external or infrastructure dependency
- Protecting a meaningful architectural boundary
- Improving testability where direct dependencies would make meaningful testing difficult
- Preventing duplication of an established shared concept
- Enforcing dependency direction

An abstraction MUST NOT be introduced solely because a design pattern could apply, because SOLID is mentioned in this constitution, or because a hypothetical future requirement might exist.

A single implementation MAY remain concrete when no meaningful substitution, dependency boundary, or variation point exists.

Duplication MAY be temporarily preferable to premature abstraction when the correct shared concept is not yet understood. Once duplicated behavior is clearly the same business or domain concept, the design SHOULD consolidate it at the appropriate boundary.

Plans MUST explain non-obvious abstractions and the concrete problem each one solves.

The number of interfaces, layers, classes, or patterns in a design is not a measure of architectural quality.

The goal is understandable, testable, changeable software with intentional boundaries and no unnecessary machinery.

## Entity Framework Core Engineering Standards

EF Core code MUST favor correctness, clear intent, and efficient database access. Do not use EF
features mechanically; choose behavior based on whether the operation actually requires database
I/O, change tracking, or entity materialization.

- **Use async only for operations that perform asynchronous database I/O.** Prefer `ToListAsync`,
  `SingleOrDefaultAsync`, `AnyAsync`, `SaveChangesAsync`, `ExecuteUpdateAsync`, and similar methods
  when database access occurs. Do not use `AddAsync` or `AddRangeAsync` by default; normal entity
  adds only modify EF's in-memory change tracker and MUST use `Add` or `AddRange`. `AddAsync` is
  appropriate only when an asynchronous value generator actually requires it.

- **Use `AsNoTracking()` for read-only entity queries.** Queries whose entities will be modified
  and persisted through the current `DbContext` MUST remain tracked. Do not add tracking when it
  provides no value.

- **Prefer projection for read models.** When callers only need part of an entity, use `Select` to
  retrieve the required fields or DTO rather than materializing the complete entity graph.

- **Keep query execution in the database.** Apply filtering, sorting, projection, and limiting
  before materializing with `ToListAsync` or similar operations. Do not load an entire dataset into
  memory and then filter it when the database can perform the operation.

- **Avoid unnecessary database work.** Use `AnyAsync()` for existence checks rather than retrieving
  entities or using `CountAsync() > 0`. Limit or paginate potentially large result sets.

- **Load relationships intentionally.** Only `Include` relationships required by the operation.
  Avoid patterns that introduce N+1 queries or unnecessarily large entity graphs. When multiple
  collection includes could produce a Cartesian explosion, evaluate projection or `AsSplitQuery()`
  rather than adding more joins blindly.

- **Treat `DbContext` as a short-lived unit of work.** Do not store it as a singleton or share it
  across threads. EF Core does not support parallel operations against the same `DbContext`; await
  database operations before using that context again.

- **Minimize database round trips.** Accumulate related tracked changes and normally call
  `SaveChangesAsync` once per logical unit of work rather than after every entity modification.

- **Prefer set-based operations for bulk changes.** When many rows can be updated or deleted
  directly in the database, consider `ExecuteUpdateAsync` or `ExecuteDeleteAsync` instead of
  loading, tracking, and modifying every entity. Be careful when mixing these operations with
  already tracked entities because they bypass EF's change tracker.

- **Propagate cancellation tokens** through async EF operations when the calling operation provides
  one.

- **Review generated query behavior when performance matters.** Consider query shape, indexes,
  number of rows returned, joins, and database round trips rather than assuming a concise LINQ
  expression is efficient.

When multiple EF Core approaches are valid, prefer the simplest approach that produces correct
SQL, minimizes unnecessary tracking and materialization, and remains clear to future maintainers.

## Spec Kit Ownership

Spec Kit is this project's sole AI-assisted feature development methodology.

It owns the complete structured feature lifecycle:

- Constitution
- Feature specification
- Clarification
- Technical planning
- Checklists where useful
- Task breakdown
- Artifact analysis
- Implementation
- Code and design review
- Convergence verification

No second planning or implementation methodology is layered on top of Spec Kit.

If implementation surfaces:

- A contradictory requirement
- A missing business rule
- An unclear workflow
- An architectural conflict
- A requirement that cannot reasonably be implemented as specified
- A conflict with this constitution

work MUST stop and return to the appropriate Spec Kit clarification or planning phase.

This rule applies equally to findings surfaced by the Code and Design Review gate (`/code-design-review`): a Must Fix finding rooted in a flawed technical plan returns to `/speckit-plan`; a Must Fix finding rooted in an unresolved or contradictory requirement returns to `/speckit-clarify`. Must Fix findings that are ordinary implementation-level defects are captured as new tasks in the feature's existing `tasks.md` — not a separate or competing task-tracking system — and resolved through `/speckit-implement`. Advisory findings from `/code-design-review` do not block completion.

The specification and plan MUST NOT be silently redesigned during implementation.

Higher-level requirement artifacts MUST NOT be normalized to match whatever code happens to exist.

### Architecture and Changeability Review

During technical planning, Spec Kit MUST evaluate the proposed design against the maintainability, extensibility, simplicity, dependency, testing, and domain-integrity principles in this constitution.

For each significant component or architectural boundary relevant to the feature, the plan MUST consider:

1. **Responsibility** — What single conceptual job does this component perform?
2. **Boundaries** — Which concerns belong here, and which explicitly do not?
3. **Dependency direction** — What does this component depend on, and are those dependencies pointing toward stable application or domain concepts where appropriate?
4. **Foreseeable variation** — Which approved or clearly anticipated cases are likely to vary?
5. **Extension cost** — What would need to change to support the next foreseeable case?
6. **Failure modeling** — How are expected parsing, validation, business, infrastructure, and integration failures represented?
7. **Domain integrity** — At what point does untrusted, incomplete, or external input become a valid domain object?
8. **Testability** — Can important behavior be tested without unnecessary infrastructure or unrelated application components?
9. **Abstraction rationale** — Does each non-trivial abstraction solve a concrete problem?
10. **Simplicity** — Is there a simpler design that preserves the required boundaries, correctness, and changeability?

The plan does not need to create abstractions for every possible future change.

It MUST, however, address variation points already implied by the approved requirements or by concrete integrations the feature is explicitly expected to support.

Architecture MUST NOT be considered acceptable solely because it compiles, passes its current tests, or satisfies the immediate happy path.

## Development Workflow

The normal feature lifecycle is:

Requirement  
→ Spec Kit specification  
→ clarification  
→ technical plan  
→ human architecture and changeability review  
→ checklist when useful  
→ task breakdown  
→ analyze  
→ human approval  
→ feature branch or worktree  
→ Spec Kit implementation using strict Red → Green → Refactor TDD per Principle IV  
→ automated build/tests  
→ code and design review (`/code-design-review`)  
→ convergence verification  
→ pull request  
→ CI  
→ human review  
→ merge

Code and design review evaluates the actual implementation against the approved specification, plan, and this constitution; it is distinct from the human architecture and changeability review earlier in this list, which evaluates the proposed design before implementation exists. Must Fix findings are captured as new tasks in the existing `tasks.md` and resolved through implementation; Advisory findings do not block progress. This step is required for application-code changes before convergence verification.

If convergence surfaces remaining required work:

Implementation  
→ convergence verification  
→ repeat until converged.

If code and design review surfaces remaining Must Fix findings:

Implementation  
→ code and design review  
→ repeat until no Must Fix findings remain, before proceeding to convergence verification.

The Spec Kit task breakdown (`tasks.md`) is the tracker for in-progress feature work. No GitHub issue is required per feature.

Before task generation is approved, the technical plan SHOULD be reviewed specifically for:

- Responsibility boundaries
- Dependency direction
- Known variation points
- Failure modeling
- Domain integrity
- Testability
- Appropriate extensibility
- Unnecessary abstraction

See `docs/development/ai-assisted-development-workflow.md` for the full workflow and project-specific validation gates.

## Governance

Confirmed Product Owner decisions and the approved PRD govern product behavior and business requirements according to Principle I.

This constitution governs engineering, architecture, testing, security, privacy, reliability, and development-process standards.

Neither source of authority MUST be silently rewritten to resolve a conflict with the other. A meaningful conflict between an approved product requirement and an engineering principle MUST be surfaced for explicit resolution.

All specifications, technical plans, task breakdowns, implementations, and pull requests MUST be consistent with these principles.

A deviation from a constitution `MUST` requires explicit documentation of:

- The principle being deviated from
- Why compliance is not reasonably achievable or would cause a more serious conflict
- The scope of the deviation
- The mitigation used instead

Where a deviation changes product behavior or acceptance criteria, it MUST also be escalated to the Product Owner.

Amendments to this constitution require documentation of the change and rationale.

Safety-related principles, including Sections V, VI, and VII, MUST NOT be weakened without explicit Product Owner and Developer approval.

Changes to maintainability or simplicity principles MUST preserve the balance between reasonable extensibility and avoiding speculative over-engineering.

**Version**: 3.2.0 | **Ratified**: 2026-08-19 | **Last Amended**: 2026-08-21
