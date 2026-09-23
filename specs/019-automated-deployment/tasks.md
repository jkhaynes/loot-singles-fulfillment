---

description: "Task list for feature 019: automated stage and production deployment"
---

# Tasks: Automated Stage and Production Deployment

**Input**: Design documents from `/specs/019-automated-deployment/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required, not optional. Constitution Principle IV (Test-Driven Development) is
NON-NEGOTIABLE, so every behavioural change is Red → Green: the test task comes before the
implementation task that makes it pass, and must fail for the right reason first.

**Manual tasks**: Tasks marked **[MANUAL]** are run by hand against Azure or GitHub, following the
runbook in [quickstart.md](quickstart.md) Part 2 — which carries the exact commands, what success
looks like, and what to do when a step fails. Each manual task names the runbook step that covers
it. They are real work with real acceptance criteria, not notes.

Keeping provisioning manual is a Product Owner decision of 2026-09-22 (spec.md Clarifications), not
a repository rule — nothing here forbids scripting it, and a later feature may.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task serves (US1–US5)
- **[MANUAL]**: Performed by a person against Azure or GitHub, not by code

## Path Conventions

Web application layout: `backend/src/`, `backend/tests/`, `frontend/src/`, workflows in
`.github/workflows/`, `Dockerfile` at the repository root.

---

## Phase 0: Blocker (must close before any implementation)

- [ ] T001 Amend PRD §40.8 so approved hosting serves the web app from the API origin rather than Azure Static Web Apps, recording that a `SameSite=Strict` session cookie cannot cross origins, in `docs/prd/Loot_Singles_Fulfillment_PRD_v0.5.md` (or its successor). **Product Owner decision — do not assume it.** See plan.md Complexity Tracking. No implementation task may start until this is closed.

---

## Phase 1: Setup

- [ ] T002 Add the `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` package reference to `backend/src/LootSingles.Api/LootSingles.Api.csproj`, matching the .NET 10 version already used by the other `Microsoft.AspNetCore.*` references
- [ ] T003 [P] Create the test folders `backend/tests/LootSingles.IntegrationTests/Hosting/` and `backend/tests/LootSingles.IntegrationTests/Health/`

---

## Phase 2: Foundational — host the application behind a TLS-terminating ingress

**Purpose**: The application cannot run in any environment until it survives being proxied, serves
the web app, and answers both health endpoints.

**⚠️ CRITICAL**: Blocks every user story.

> **T047 and T048 have out-of-sequence IDs because they were added after the first numbering, by
> `/speckit-analyze`, which found that nothing created the `/health/database` endpoint even though
> the plan, the contract, T014 and T029 all depend on it. IDs are never reused or renumbered, so
> read this phase in the order written, not in numeric order.**

### Tests (write first, confirm they fail)

- [ ] T004 [P] Write a failing test that a request carrying `X-Forwarded-Proto: https` is served rather than redirected, and that one without it still redirects, in `backend/tests/LootSingles.IntegrationTests/Hosting/ForwardedHeadersTests.cs`. Fails today: `UseHttpsRedirection()` runs with no forwarded-header handling, so the proxied request is answered with a 307
- [ ] T005 [P] Write a failing test that `GET /health` returns 200 anonymously **and still returns 200 when the database is unreachable**, in `backend/tests/LootSingles.IntegrationTests/Health/HealthEndpointTests.cs`. Point the factory at an unreachable connection string for the second case, per contracts/health-api.md
- [ ] T006 [P] Write a failing test that `/` and a deep link such as `/orders/42` return the web app's HTML, `GET /api/unknown` returns **404 not HTML**, and `GET /api/orders` unauthenticated returns **401 not HTML**, in `backend/tests/LootSingles.IntegrationTests/Hosting/SpaFallbackTests.cs`. Use `WithWebHostBuilder(b => b.UseWebRoot(<temp dir>))` seeded with a fixture `index.html` containing `id="root"`, so the test does not depend on `frontend/` having been built
- [ ] T047 [P] Write a failing test that `GET /health/database` returns **200** against a reachable database and **503** against an unreachable one, that `/health` returns 200 in the *same* run where `/health/database` returns 503 — the deliberate asymmetry, pinned — and that the 503 body contains no connection string, server or database name, identity client id, exception message or stack trace, in `backend/tests/LootSingles.IntegrationTests/Health/DatabaseHealthEndpointTests.cs`. Per contracts/health-api.md (FR-024, FR-025, SC-007)

### Implementation

- [ ] T007 Register forwarded headers first in the pipeline in `backend/src/LootSingles.Api/Program.cs`: accept `ForwardedHeaders.XForwardedProto` only, clear `KnownNetworks` and `KnownProxies`, placed before `app.UseHttpsRedirection()`. Add a comment recording why trusting the header is safe here — only the environment's ingress can reach the container port (research.md §2). Makes T004 pass
- [ ] T008 Add `GET /health` as an anonymous endpoint performing **no database access**, returning 200 with an empty body, in `backend/src/LootSingles.Api/Program.cs`. Makes T005 pass
- [ ] T009 Add `UseDefaultFiles()`, `UseStaticFiles()`, then after `MapControllers()` add `MapFallback("/api/{**path}", () => Results.NotFound())` **before** `MapFallbackToFile("index.html")`, in `backend/src/LootSingles.Api/Program.cs`. Ordering is the requirement (FR-004); reversing it returns HTML for unmatched API routes. Makes T006 pass
- [ ] T048 Add `GET /health/database` as an anonymous endpoint in `backend/src/LootSingles.Api/Program.cs`: one lightweight read (`Employees.AnyAsync()`), returning 200 with an empty body or **503 with a fixed body**, writing the reason to `ILogger<T>` and never to the caller. **The container probe must never use it** — that is `/health` (T008), and wiring this one to the probe reintroduces the exact failure FR-023 forbids. Makes T047 pass

**Checkpoint**: The application can be hosted behind a proxy, serves both the API and the web app,
and can report separately that it is alive (`/health`) and that it can reach its database
(`/health/database`). T014 and T029 both depend on the second endpoint existing.

---

## Phase 3: User Story 1 - Pick Orders at the Shop (Priority: P1) 🎯 MVP

**Goal**: The application is reachable from a store device over a stable secure address, with no
developer machine running, and a picker can complete real work on it.

**Independent Test**: Deploy by hand once, then sign in from a store device over the public address
and complete a pick.

### Tests

- [ ] T010 [US1] Write a failing test that the `migrate` command applies pending migrations, is safe to run twice, and on failure reports without echoing the connection string, in `backend/tests/LootSingles.IntegrationTests/Hosting/MigrateCommandTests.cs`. Follow the assertion style of the existing `Configuration/DatabaseConfigurationTests.cs` for the no-echo check

### Implementation

- [ ] T011 [US1] Create `backend/src/LootSingles.Api/MigrateCommand.cs` mirroring `BootstrapAdminCommand.cs`: apply pending migrations, log their names through `ILogger<T>`, return 0 or 1, never echo the connection string
- [ ] T012 [US1] Register `MigrateCommand` in the service collection and dispatch on the `migrate` argument in `backend/src/LootSingles.Api/Program.cs`, beside the existing `bootstrap-admin` branch and before the HTTP pipeline is configured. Makes T010 pass
- [ ] T013 [US1] Create `Dockerfile` at the repository root: multi-stage with `node:24-alpine` building `frontend/`, the .NET SDK publishing **`backend/src/LootSingles.Api/LootSingles.Api.csproj`** (not the solution — it pulls in the E2E host and Testcontainers), and a final `mcr.microsoft.com/dotnet/aspnet:10.0` stage copying the web build to `wwwroot`, setting `ASPNETCORE_HTTP_PORTS=8080`, running as a **non-root** user, with `ENTRYPOINT ["dotnet", "LootSingles.Api.dll"]` and **no `CMD`** so the image also runs `migrate` and `bootstrap-admin`
- [ ] T014 [US1] Verify the built image locally per quickstart.md Part 1: the five routing checks, `whoami` is not root, `migrate` without a connection string fails without echoing one, and — the check that matters most — `/health` still returns 200 while `/health/database` returns 503 against a stopped database

### Provisioning (both environments)

- [ ] T015 [US1] [MANUAL] Verify **before creating anything**: that a subnet delegated to `Microsoft.App/environments` accepts a `Microsoft.Sql` service endpoint; the Azure SQL free-offer allowance and Basic price in region; the Container Apps free grant and overage rate; the Log Analytics free allowance; and that Basic point-in-time restore covers ≥ 7 days (FR-031). **If the service endpoint is not permitted, stop and raise it** — the private-endpoint fallback at ~$7–8/month is a Product Owner cost decision — **runbook: Part 2 C0**
- [ ] T016 [US1] [MANUAL] Create per environment: resource group, virtual network, `/27` subnet delegated to `Microsoft.App/environments` with the `Microsoft.Sql` service endpoint, and **both** managed identities (`id-loot-singles-<env>-app`, `id-loot-singles-<env>-migrate`) — **runbook: Part 2 C1–C3**
- [ ] T017 [US1] [MANUAL] Create per environment: SQL server with **Entra-only authentication** and the database; add the virtual network rule naming the subnet; confirm **no `0.0.0.0` rule exists** and public access is default-deny (FR-020); confirm point-in-time restore ≥ 7 days by reading the policy off the created database (FR-031, SC-012) — **runbook: Part 2 C4–C5**
- [ ] T018 [US1] [MANUAL] Create per environment: Log Analytics workspace, then the Container Apps environment against the subnet with logs pointed at that workspace, then the Container App on the **app** identity and the migrate job on the **migrate** identity. Both connection strings use managed-identity auth and differ only in `User Id=<clientId>` — neither contains a password (FR-019) — **runbook: Part 2 C6–C7**
- [ ] T019 [US1] [MANUAL] Through a temporary firewall rule for your own machine, create both database users and grant their roles — `db_datareader`/`db_datawriter` for the app identity, plus `db_ddladmin` for the migrate identity, **neither `db_owner`** (FR-021) — then remove that rule and run the migrate job once — **runbook: Part 2 C8**
- [ ] T020 [US1] [MANUAL] Run a throwaway bootstrap job on the **migrate** identity to create the first manager account, delete the job, and change the PIN at first sign-in (FR-032) — **runbook: Part 2 C10**
- [ ] T021 [US1] [MANUAL] Deploy the image by hand to production, then sign in **on a phone** over the public address, claim an order and record a pick — the first real write through the application identity (SC-001)
- [ ] T022 [US1] [MANUAL] Configure the custom domain: a `CNAME` for the chosen subdomain and the `TXT` ownership record in Namecheap, leaving the storefront's records untouched; confirm the certificate is issued and the address loads securely (FR-003) — **runbook: Part 2 C13**

**Checkpoint**: The shop can use the application. Everything after this is automation and hardening.

---

## Phase 4: User Story 2 - A Merge Reaches Stage by Itself (Priority: P2)

**Goal**: A merged pull request appears on stage with zero manual actions.

**Independent Test**: Merge a pull request making a visible change; confirm it is live on stage with
nobody acting after the merge.

### Tests

- [ ] T023 [P] [US2] Write a failing text assertion that **`deploy-stage.yml`** declares a `concurrency` group with `cancel-in-progress: false`, in `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`, following the `DatabaseConfigurationTests` precedent. Fails today: the file does not exist. Scoped to stage only so it goes green inside this phase — asserting both workflows here would leave the suite red for the whole of Phase 4, which reads as a broken build rather than intentional Red. Production's group is asserted in T028 (research.md §11, §16)

### Implementation

- [ ] T024 [US2] Add a `workflow_call:` trigger to `.github/workflows/pr-quality-gate.yml` so the deploy workflow reuses the existing five jobs rather than copying them
- [ ] T025 [US2] Create `.github/workflows/deploy-stage.yml` per contracts/deployment.md: trigger on push to `main` plus manual dispatch; `concurrency: deploy-stage` with `cancel-in-progress: false`; call the quality gate; build and push `ghcr.io/jkhaynes/loot-singles-fulfillment:sha-<commit>`; then under `environment: stage` sign in by OIDC, run the migrate job and wait, update the Container App, run the four-check smoke test, and **on any failure reactivate the previous revision** (FR-017). Makes half of T023 pass
- [ ] T026 [US2] [MANUAL] Create the GitHub `stage` environment: plain variables (subscription, tenant and client ids, resource group, app and job names, URL), an OIDC federated credential scoped to **stage's resource group only** (FR-006), no reviewers, deployment branches restricted to `main` — **runbook: Part 2 C11–C12**
- [ ] T027 [US2] [MANUAL] Verify end to end: merge a pull request with a visible change and confirm it reaches stage with **zero** manual actions (SC-002); then merge a change that breaks the quality checks and confirm stage is **not** updated

**Checkpoint**: Stage keeps itself current.

---

## Phase 5: User Story 3 - A Production Release Is Deliberate and Approved (Priority: P2)

**Goal**: Production changes only when a person names a commit and approves it, and the approved
version is exactly what ships.

**Independent Test**: Start a release naming a specific commit, confirm nothing changes while it
waits, approve, and confirm production runs exactly that commit.

### Tests

- [ ] T028 [P] [US3] Write failing text assertions that `.github/workflows/deploy-production.yml` declares a `commit` input with `required: true` and **no `default`**, that its gated job's `name` interpolates that input, and that it declares a `concurrency` group with `cancel-in-progress: false` (the production half of the check T023 scoped to stage), in `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`. A default would silently restore the approve-a-lookup bug (research.md §10, §11)

### Implementation

- [ ] T029 [US3] Create `.github/workflows/deploy-production.yml` per contracts/deployment.md: manual dispatch only; a required `commit` input with no default; `concurrency: deploy-production`; **one job** carrying `environment: production` so nothing runs before approval, its `name` interpolating the commit; deploying `ghcr.io/…:sha-<commit>` with the same migrate → update → smoke → rollback sequence, and `/health/database` added to the smoke test (FR-024). Makes T028 pass
- [ ] T030 [US3] [MANUAL] Create the GitHub `production` environment: plain variables, an OIDC credential scoped to **production's resource group only**, a required reviewer, deployment branches restricted to `main`, and **"Prevent self-review" left unchecked** — Product Owner decision 2026-09-22 (spec.md Clarifications) — **runbook: Part 2 C11–C12**
- [ ] T031 [US3] [MANUAL] Verify the gate: confirm a merge to `main` leaves production unchanged (SC-003); start a release, confirm the commit is visible in the job list before approving and that production is untouched while it waits; **merge a different change while it waits** and confirm the release still deploys the commit it named (SC-004); then approve and confirm production runs exactly that commit

- [ ] T049 [US3] [MANUAL] Verify a release is safe to issue during a shift: with an order claimed and at least one outcome recorded on a device, run a production release to completion, then confirm on that device that the claim and every recorded outcome survived and that the picker can continue by retrying. One failed request is acceptable; losing recorded work is not (FR-030, SC-011)

**Checkpoint**: Production is reachable only through a deliberate, approved, named release, and
releasing during a shift costs a picker at most a retry.

---

## Phase 6: User Story 4 - A Picker Stays Signed In (Priority: P3)

**Goal**: A session survives the application going idle and being released over.

**Independent Test**: Sign in, let the application go idle, return and confirm the session survived;
repeat across a release.

### Tests

- [ ] T032 [US4] Write a failing test that a cookie issued by one hosted instance is still accepted by a **new** instance sharing the same database, in `backend/tests/LootSingles.IntegrationTests/Hosting/DataProtectionPersistenceTests.cs`. Fails today: keys are held in memory, so the second instance rejects the cookie with 401

### Implementation

- [ ] T033 [US4] Implement `IDataProtectionKeyContext` on `LootSinglesDbContext` with a `DbSet<DataProtectionKey> DataProtectionKeys`, in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` (data-model.md)
- [ ] T034 [US4] Generate the additive EF Core migration creating the `DataProtectionKeys` table in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`. Additive only — rollback restores the image and never the schema (FR-016)
- [ ] T035 [US4] Register `AddDataProtection().PersistKeysToDbContext<LootSinglesDbContext>().SetApplicationName(<fixed name>)` in `backend/src/LootSingles.Api/Program.cs`. The application name is a constant: changing it invalidates every active session. Makes T032 pass
- [ ] T036 [US4] [MANUAL] Verify against a real environment: sign in, trigger a release, and confirm you are still signed in afterwards (SC-005)

**Checkpoint**: Releases and idle periods no longer sign pickers out.

---

## Phase 7: User Story 5 - Investigate Something the Shop Reported (Priority: P3)

**Goal**: What the application recorded is still findable days later, in either environment.

**Independent Test**: Cause a recognisable failure, wait until it has left any live view, then find
it.

- [ ] T037 [US5] [MANUAL] Confirm in **both** environments that application log lines reach the Log Analytics workspace and are searchable there, not merely visible in the live stream (FR-027, SC-006) — **runbook: Part 2 Part D**
- [ ] T038 [US5] [MANUAL] Read a sample of retained records in both environments and confirm they contain no customer PII, PINs, tokens, secrets or connection strings (FR-022)

**Checkpoint**: A report from the shop can be investigated after the fact.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T039 [P] Add two text assertions to `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`: that **no tracked file contains a `0.0.0.0` firewall rule** (FR-020), and that no tracked file contains a credential-shaped value — a SQL connection string with `Password=`, or a `Uid=`/`User ID=` paired with a password (FR-019, SC-009). Both are guards rather than Red → Green pairs: they pass when written and exist to fail on a later careless edit (research.md §16). The design means neither should ever be possible — Entra-only authentication leaves no password to commit — but nothing proved it until now
- [ ] T040 [P] [MANUAL] Confirm privacy parity between stage and production side by side: both default-deny database access, both split the application and migration identities, both hash PINs, both log packing-slip access (FR-029, SC-010) — **runbook: Part 2 Part D**
- [ ] T041 [P] [MANUAL] Create the $5 budget alert and record the first month's actual cost against the $5–8 estimate (FR-028, SC-008)
- [ ] T042 [P] Update `README.md` with how to run the container locally and where the deployment workflows live, and `CLAUDE.md` if any rule of engagement changed
- [ ] T043 Run the full backend regression — `dotnet test` for both test projects — plus `npm run build` and the oxlint check for the frontend, confirming no existing test broke
- [ ] T044 Run CSharpier over the changed C# files, matching the repository's existing formatting gate
- [ ] T045 Work through quickstart.md end to end as written, correcting anything that does not match what was built
- [ ] T046 Run `/branch-review` and resolve every Required finding before `/speckit-converge` (CLAUDE.md Branch Review Gate)

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 0 (blocker)**: T001 gates everything. It is a Product Owner decision, not engineering work
- **Phase 1 (setup)**: after T001
- **Phase 2 (foundational)**: after Phase 1. **Blocks every user story** — nothing can be hosted until the application survives a proxy and serves the web app
- **Phase 3 (US1)**: after Phase 2. Delivers the MVP
- **Phase 4 (US2)** and **Phase 5 (US3)**: after Phase 3, because both deploy the image that Phase 3 creates and target the environments Phase 3 provisions
- **Phase 6 (US4)**: after Phase 2 for the code; T036 needs Phase 4 or 5 for a real release
- **Phase 7 (US5)**: after T018 creates the workspaces
- **Phase 8 (polish)**: after the stories it verifies

### Notable ordering constraints

- T023 and T028 are both text assertions in the same file; write T023 first and extend the file in T028 rather than creating it twice
- T025 must precede T029: production reuses the smoke-test and rollback steps stage establishes
- T023 covers stage only and T028 covers production, so each goes green inside its own phase. An earlier draft had T023 assert both, which left the suite red across the Phase 4/5 boundary
- T031's middle step — merging a change while a release waits for approval — is the single most valuable verification in this feature. It is the failure mode the whole promotion design exists to prevent (research.md §10). Do not skip it because the release "obviously" works

### Within each story

Test task → implementation task → manual verification. Never the reverse (constitution Principle IV).

### Parallel opportunities

- T004, T005, T006 are three separate test files with no shared state
- T007, T008, T009 all edit `Program.cs` and are therefore **strictly sequential**, despite belonging to the same phase
- T023 and T028 are marked [P] relative to other stories' work but not to each other (same file)
- T039–T042 are independent of one another

---

## Implementation Strategy

### MVP (Phases 0–3)

Close the PRD amendment, make the application hostable, package it, provision both environments, and
deploy production by hand. **Stop and validate**: a picker completes a real pick on a store device.
At this point the shop has the product, deployed manually.

### Increment 2 (Phase 4)

Stage deploys itself. Removes the manual step from the low-risk environment first, so the automation
is exercised where a mistake costs nothing.

### Increment 3 (Phase 5)

Production deploys by approval, reusing the sequence stage has already proven.

### Increment 4 (Phases 6–7)

Sessions survive restarts; retained records are confirmed searchable.

### A note on Phase 6's priority

US4 is P3 because being signed out is disruptive rather than destructive, and that is the correct
specification priority. In practice, a scale-to-zero container signs every picker out after any quiet
spell (research.md §5), so consider pulling Phase 6 forward if the shop starts daily use before the
automation phases are finished.

---

## Notes

- `[P]` means different files and no dependency — verify both before running tasks concurrently
- Every `[MANUAL]` task has acceptance criteria and a success criterion it satisfies; none is a
  formality
- Commit after each task or logical group, and confirm before committing (repository convention)
- Infrastructure figures in the design documents come from documentation, not measurement. T015 is
  where they become facts — treat a mismatch as a finding, not a rounding error
