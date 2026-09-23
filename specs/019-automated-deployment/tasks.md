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

- [X] T001 Amend PRD §40.8 so approved hosting serves the web app from the API origin rather than Azure Static Web Apps, recording that a `SameSite=Strict` session cookie cannot cross origins. **Done 2026-09-22**: approved by the Product Owner as amendment **A17** and folded into [`docs/prd/Loot_Singles_Fulfillment_PRD_v0.6.md`](../../docs/prd/Loot_Singles_Fulfillment_PRD_v0.6.md), now the authoritative PRD. Rationale kept in [`Loot_Singles_Fulfillment_PRD_v0.6-proposed-amendments.md`](../../docs/prd/Loot_Singles_Fulfillment_PRD_v0.6-proposed-amendments.md). `CLAUDE.md`, `README.md` and the constitution (v3.5.1) now point at v0.6. **Implementation is unblocked.**

---

## Phase 1: Setup

- [X] T002 Add the `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` package reference to `backend/src/LootSingles.Api/LootSingles.Api.csproj`, matching the .NET 10 version already used by the other `Microsoft.AspNetCore.*` references
- [X] T003 [P] Create the test folders `backend/tests/LootSingles.IntegrationTests/Hosting/` and `backend/tests/LootSingles.IntegrationTests/Health/`

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

- [X] T004 [P] Write a failing test that a request carrying `X-Forwarded-Proto: https` is served rather than redirected, and that one without it still redirects, in `backend/tests/LootSingles.IntegrationTests/Hosting/ForwardedHeadersTests.cs`. Fails today: `UseHttpsRedirection()` runs with no forwarded-header handling, so the proxied request is answered with a 307
- [X] T005 [P] Write a failing test that `GET /health` returns 200 anonymously **and still returns 200 when the database is unreachable**, in `backend/tests/LootSingles.IntegrationTests/Health/HealthEndpointTests.cs`. Point the factory at an unreachable connection string for the second case, per contracts/health-api.md
- [X] T006 [P] Write a failing test that `/` and a deep link such as `/orders/42` return the web app's HTML, `GET /api/unknown` returns **404 not HTML**, and `GET /api/orders` unauthenticated returns **401 not HTML**, in `backend/tests/LootSingles.IntegrationTests/Hosting/SpaFallbackTests.cs`. Use `WithWebHostBuilder(b => b.UseWebRoot(<temp dir>))` seeded with a fixture `index.html` containing `id="root"`, so the test does not depend on `frontend/` having been built
- [X] T047 [P] Write a failing test that `GET /health/database` returns **200** against a reachable database and **503** against an unreachable one, that `/health` returns 200 in the *same* run where `/health/database` returns 503 — the deliberate asymmetry, pinned — and that the 503 body contains no connection string, server or database name, identity client id, exception message or stack trace, in `backend/tests/LootSingles.IntegrationTests/Health/DatabaseHealthEndpointTests.cs`. Per contracts/health-api.md (FR-024, FR-025, SC-007)

### Implementation

- [X] T007 Register forwarded headers first in the pipeline in `backend/src/LootSingles.Api/Program.cs`: accept `ForwardedHeaders.XForwardedProto` only, clear `KnownNetworks` and `KnownProxies`, placed before `app.UseHttpsRedirection()`. Add a comment recording why trusting the header is safe here — only the environment's ingress can reach the container port (research.md §2). Makes T004 pass
- [X] T008 Add `GET /health` as an anonymous endpoint performing **no database access**, returning 200 with an empty body, in `backend/src/LootSingles.Api/Program.cs`. Makes T005 pass
- [X] T009 Add `UseDefaultFiles()`, `UseStaticFiles()`, then after `MapControllers()` add `MapFallback("/api/{**path}", () => Results.NotFound())` **before** `MapFallbackToFile("index.html")`, in `backend/src/LootSingles.Api/Program.cs`. Ordering is the requirement (FR-004); reversing it returns HTML for unmatched API routes. Makes T006 pass
- [X] T048 Add `GET /health/database` as an anonymous endpoint in `backend/src/LootSingles.Api/Program.cs`: one lightweight read (`Employees.AnyAsync()`), returning 200 with an empty body or **503 with a fixed body**, writing the reason to `ILogger<T>` and never to the caller. **The container probe must never use it** — that is `/health` (T008), and wiring this one to the probe reintroduces the exact failure FR-023 forbids. Makes T047 pass

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

- [X] T010 [US1] Write a failing test that the `migrate` command applies pending migrations, is safe to run twice, and on failure reports without echoing the connection string, in `backend/tests/LootSingles.IntegrationTests/Hosting/MigrateCommandTests.cs`. Follow the assertion style of the existing `Configuration/DatabaseConfigurationTests.cs` for the no-echo check

### Implementation

- [X] T011 [US1] Create `backend/src/LootSingles.Api/MigrateCommand.cs` mirroring `BootstrapAdminCommand.cs`: apply pending migrations, log their names through `ILogger<T>`, return 0 or 1, never echo the connection string
- [X] T012 [US1] Register `MigrateCommand` in the service collection and dispatch on the `migrate` argument in `backend/src/LootSingles.Api/Program.cs`, beside the existing `bootstrap-admin` branch and before the HTTP pipeline is configured. Makes T010 pass
- [X] T013 [US1] Create `Dockerfile` at the repository root: multi-stage with `node:24-alpine` building `frontend/`, the .NET SDK publishing **`backend/src/LootSingles.Api/LootSingles.Api.csproj`** (not the solution — it pulls in the E2E host and Testcontainers), and a final `mcr.microsoft.com/dotnet/aspnet:10.0` stage copying the web build to `wwwroot`, setting `ASPNETCORE_HTTP_PORTS=8080`, running as a **non-root** user, with `ENTRYPOINT ["dotnet", "LootSingles.Api.dll"]` and **no `CMD`** so the image also runs `migrate` and `bootstrap-admin`
- [X] T014 [US1] Verify the built image locally per quickstart.md Part 1: the five routing checks, `whoami` is not root, `migrate` without a connection string fails without echoing one, and — the check that matters most — `/health` still returns 200 while `/health/database` returns 503 against a stopped database

### Provisioning

> **Amended 2026-09-23.** T016 and T018 were written as "create per environment" and were done that
> way for stage before the cost check in C6 found that each Container Apps environment carries a
> Standard static public IPv4 at $3.65/month. Two of them plus production's database came to $12.20
> against FR-028's $10 ceiling.
>
> The Product Owner chose to **share one Container Apps environment, virtual network and Log
> Analytics workspace** between both environments (spec.md Clarifications), which brings it to $8.55
> and keeps stage. Those three resources are now built **once**, in `rg-loot-singles-shared`;
> identities, SQL servers, databases, container apps and migrate jobs remain per environment.
>
> Stage was rebuilt into that layout on 2026-09-23. The tasks below are marked against the amended
> shape, not the original wording.

- [ ] T015 [US1] [MANUAL] Verify **before creating anything**: that a subnet delegated to `Microsoft.App/environments` accepts a `Microsoft.Sql` service endpoint; the Azure SQL free-offer allowance and Basic price in region; the Container Apps free grant and overage rate; the Log Analytics free allowance; and that Basic point-in-time restore covers ≥ 7 days (FR-031). **If the service endpoint is not permitted, stop and raise it** — the private-endpoint fallback at ~$7–8/month is a Product Owner cost decision — **runbook: Part 2 C0**. **Mostly done 2026-09-23**: the service endpoint **is** permitted on a delegated subnet (verified live, research.md §8), so the cost decision does not arise. Pricing verified against Azure's public Retail Prices API and the billing documentation — SQL Basic $0.161/day in `eastus2`, Container Apps overage $0.000024/vCPU-s and $0.000003/GiB-s, free grant 180,000 vCPU-s / 360,000 GiB-s / 2M requests per subscription per month. The $0.10/hour environment management charge attaches to private endpoints and planned maintenance, neither of which this design uses. **The virtual-network meter question is answered**, and the answer broke the cost model: the network itself bills ~$0.03, but every Container Apps environment with external ingress carries a **Standard static public IPv4 at $3.65/month** that no documentation surfaced. Two environments would have been $12.20 against FR-028's $10 ceiling. Resolved by sharing one Container Apps environment (spec.md Clarifications 2026-09-23) — verified live as exactly one public IP in the subscription, `capp-svc-lb-ip`, bringing the total to **$8.55/month**. Point-in-time restore **confirmed at 7 days** on both databases (2026-09-23). Still outstanding, and not answerable from documentation: the **stage free-offer SQL allowance**, read from Cost Analysis ~24h after the rebuilt environment has been running (runbook C6).
- [X] T016 [US1] [MANUAL] Create per environment: resource group, virtual network, `/27` subnet delegated to `Microsoft.App/environments` with the `Microsoft.Sql` service endpoint, and **both** managed identities (`id-loot-singles-<env>-app`, `id-loot-singles-<env>-migrate`) — **runbook: Part 2 C1–C3**
- [X] T017 [US1] [MANUAL] Create per environment: SQL server with **Entra-only authentication** and the database; add the virtual network rule naming the subnet; confirm **no `0.0.0.0` rule exists** and public access is default-deny (FR-020); confirm point-in-time restore ≥ 7 days by reading the policy off the created database (FR-031, SC-012) — **runbook: Part 2 C4–C6**
- [X] T018 [US1] [MANUAL] Create per environment: Log Analytics workspace, then the Container Apps environment against the subnet with logs pointed at that workspace, then the Container App on the **app** identity and the migrate job on the **migrate** identity. Both connection strings use managed-identity auth and differ only in `User Id=<clientId>` — neither contains a password (FR-019) — **runbook: Part 2 C7–C10**
> **Re-sequenced 2026-09-23.** T019–T022 were written expecting production to be deployed by hand
> first, with automation added afterwards. The build ran the other way round: both deployment
> workflows exist and both environments are provisioned, while no image has ever been published. A
> hand-deployment would now mean building and pushing an image outside CI purely to retire a task,
> which is more work and less proof than letting the first real deployment do it. The requirements
> below are unchanged — only the moment each one is satisfied has moved. **None of these tasks was
> removed; each is the sole coverage for its requirement.**

- [ ] T019 [US1] [MANUAL] Create both database users and grant their roles — `db_datareader`/`db_datawriter` for the app identity, plus `db_ddladmin` for the migrate identity, **neither `db_owner`** (FR-021) — **runbook: Part 2 C11**. **Grants done 2026-09-23** on both databases, and the temporary firewall rules removed (the portal's query editor re-adds one on every visit — Part D). **Outstanding**: confirmation that the split is actually correct, which is what `/health/database` returning 200 proves on the first deployment. The migrate job is no longer run by hand here — `deploy-stage.yml` points it at the commit's image, runs it, and fails the deploy if it fails.
- [ ] T020 [US1] [MANUAL] Create the first manager account on the **migrate** identity and change the PIN at first sign-in (FR-032). Re-sequenced: this now runs **after** the first successful deployment, as a one-off execution of the already-deployed image's `bootstrap-admin` command, rather than as a throwaway job created beforehand. Same identity, same requirement, one fewer resource to create and delete. **Runbook gap found 2026-09-23**: this task pointed at C10, which is the migrate job — the runbook has no bootstrap step at all, so FR-032 is uncovered there. Writing one before the flow has ever been run is how the nine portal defects got in; it is written as part of T045, from an actual run.
- [ ] T021 [US1] [MANUAL] After the first **production** release, sign in **on a phone** over the public address, claim an order and record a pick — the first real write through the application identity (SC-001). The hand-deployment this task originally opened with is now done by `deploy-production.yml`; the verification is the part that matters and is unchanged.
- [ ] T022 [US1] [MANUAL] Configure the custom domain: a `CNAME` for the chosen subdomain and the `TXT` ownership record in Namecheap, leaving the storefront's records untouched; confirm the certificate is issued and the address loads securely (FR-003) — **runbook: Part 2 C15**. Unaffected by the re-sequencing, and deliberately left until after the first deployment.

**Checkpoint**: The shop can use the application. This checkpoint now falls **after** Phases 4–5 rather than before them, because the first deployment is what carries T019–T021 over the line.

---

## Phase 4: User Story 2 - A Merge Reaches Stage by Itself (Priority: P2)

**Goal**: A merged pull request appears on stage with zero manual actions.

**Independent Test**: Merge a pull request making a visible change; confirm it is live on stage with
nobody acting after the merge.

### Tests

- [X] T023 [P] [US2] Write a failing text assertion that **`deploy-stage.yml`** declares a `concurrency` group with `cancel-in-progress: false`, in `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`, following the `DatabaseConfigurationTests` precedent. Fails today: the file does not exist. Scoped to stage only so it goes green inside this phase — asserting both workflows here would leave the suite red for the whole of Phase 4, which reads as a broken build rather than intentional Red. Production's group is asserted in T028 (research.md §11, §16)

### Implementation

- [X] T024 [US2] Add a `workflow_call:` trigger to `.github/workflows/pr-quality-gate.yml` so the deploy workflow reuses the existing five jobs rather than copying them
- [X] T025 [US2] Create `.github/workflows/deploy-stage.yml` per contracts/deployment.md: trigger on push to `main` plus manual dispatch; `concurrency: deploy-stage` with `cancel-in-progress: false`; call the quality gate; build and push `ghcr.io/jkhaynes/loot-singles-fulfillment:sha-<commit>`; then under `environment: stage` sign in by OIDC, run the migrate job and wait, update the Container App, run the four-check smoke test, and **on any failure reactivate the previous revision** (FR-017). Makes half of T023 pass
- [X] T026 [US2] [MANUAL] Create the GitHub `stage` environment: plain variables (subscription, tenant and client ids, resource group, app and job names, URL), an OIDC federated credential scoped to **stage's container app and migrate job specifically** (FR-006, as amended 2026-09-23 — resource-group scope stopped being the boundary once the environments began sharing one Container Apps environment), no reviewers, deployment branches restricted to `main` — **runbook: Part 2 C12–C13**. **Done 2026-09-23**: all seven variables set, subject `repo:jkhaynes@7768504/loot-singles-fulfillment@1340223419:environment:stage`, Contributor on `ca-loot-singles-stage` and `caj-loot-singles-stage-migrate` only.
- [ ] T027 [US2] [MANUAL] Verify end to end: merge a pull request with a visible change and confirm it reaches stage with **zero** manual actions (SC-002); then merge a change that breaks the quality checks and confirm stage is **not** updated

**Checkpoint**: Stage keeps itself current.

---

## Phase 5: User Story 3 - A Production Release Is Deliberate and Approved (Priority: P2)

**Goal**: Production changes only when a person names a commit and approves it, and the approved
version is exactly what ships.

**Independent Test**: Start a release naming a specific commit, confirm nothing changes while it
waits, approve, and confirm production runs exactly that commit.

### Tests

- [X] T028 [P] [US3] Write failing text assertions that `.github/workflows/deploy-production.yml` declares a `commit` input with `required: true` and **no `default`**, that its gated job's `name` interpolates that input, and that it declares a `concurrency` group with `cancel-in-progress: false` (the production half of the check T023 scoped to stage), in `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`. A default would silently restore the approve-a-lookup bug (research.md §10, §11)

### Implementation

- [X] T029 [US3] Create `.github/workflows/deploy-production.yml` per contracts/deployment.md: manual dispatch only; a required `commit` input with no default; `concurrency: deploy-production`; **one job** carrying `environment: production` so nothing runs before approval, its `name` interpolating the commit; deploying `ghcr.io/…:sha-<commit>` with the same migrate → update → smoke → rollback sequence, and `/health/database` added to the smoke test (FR-024). Makes T028 pass
- [X] T030 [US3] [MANUAL] Create the GitHub `production` environment: plain variables, an OIDC credential scoped to **production's container app and migrate job specifically** (FR-006, as amended 2026-09-23), a required reviewer, deployment branches restricted to `main`, and **"Prevent self-review" left unchecked** — Product Owner decision 2026-09-22 (spec.md Clarifications) — **runbook: Part 2 C12–C13**. **Done 2026-09-23**: all seven variables set, subject `…:environment:production`, reviewer `jkhaynes` with `prevent_self_review: false`, Contributor on `ca-loot-singles-prod` and `caj-loot-singles-prod-migrate` only. Client ids differ from stage's, so neither credential reaches the other environment.
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

- [X] T032 [US4] Write a failing test that a cookie issued by one hosted instance is still accepted by a **new** instance sharing the same database, in `backend/tests/LootSingles.IntegrationTests/Hosting/DataProtectionPersistenceTests.cs`. Fails today: keys are held in memory, so the second instance rejects the cookie with 401

### Implementation

- [X] T033 [US4] Implement `IDataProtectionKeyContext` on `LootSinglesDbContext` with a `DbSet<DataProtectionKey> DataProtectionKeys`, in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` (data-model.md)
- [X] T034 [US4] Generate the additive EF Core migration creating the `DataProtectionKeys` table in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`. Additive only — rollback restores the image and never the schema (FR-016)
- [X] T035 [US4] Register `AddDataProtection().PersistKeysToDbContext<LootSinglesDbContext>().SetApplicationName(<fixed name>)` in `backend/src/LootSingles.Api/Program.cs`. The application name is a constant: changing it invalidates every active session. Makes T032 pass
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

- [X] T039 [P] Add two text assertions to `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs`: that **no tracked file contains a `0.0.0.0` firewall rule** (FR-020), and that no tracked file contains a credential-shaped value — a SQL connection string carrying a password assignment, or a user-id assignment paired with a password (FR-019, SC-009). Both are guards rather than Red → Green pairs: they pass when written and exist to fail on a later careless edit (research.md §16). The design means neither should ever be possible — Entra-only authentication leaves no password to commit — but nothing proved it until now
- [ ] T040 [P] [MANUAL] Confirm privacy parity between stage and production side by side: both default-deny database access, both split the application and migration identities, both hash PINs, both log packing-slip access (FR-029, SC-010) — **runbook: Part 2 Part D**
- [ ] T041 [P] [MANUAL] Create the budget alert and record the first month's actual cost against the $8.55 estimate (FR-028, SC-008). **Budget done 2026-09-23**: $10/month at subscription scope — FR-028's ceiling — with actual-cost notifications at 80%, 90% and 100% to the Product Owner's email (Product Owner decision; supersedes the original $5 figure, which predated the $3.65 public-IP finding). **Outstanding**: the first month's actual cost, which cannot be recorded until a month has elapsed.
- [ ] T042 [P] Update `README.md` with how to run the container locally and where the deployment workflows live, and `CLAUDE.md` if any rule of engagement changed
- [X] T043 Run the full backend regression — `dotnet test` for both test projects — plus `npm run build` and the oxlint check for the frontend, confirming no existing test broke
- [X] T044 Run CSharpier over the changed C# files, matching the repository's existing formatting gate
- [ ] T045 Work through quickstart.md end to end as written, correcting anything that does not match what was built. **Pass done 2026-09-23** over everything checkable without a deployment — Parts 1, A, B, C, D, E, F, G read line by line, with Part B's network and database claims verified live against Azure. Corrected: Part B named the production app registration `github-loot-singles-prod` (it is `github-loot-singles-production`) and listed no container image; Part 2's header said to run everything twice, which stopped being true when the environment became shared; **Part E still described one resource group per environment**, the change the 2026-09-23 clarification called for and never received; Part 1's non-root check ran `docker run … whoami`, which with an `ENTRYPOINT` and no `CMD` starts the web server instead of reporting the user, so it could never have failed; three stale cross-references (Part F job logs → C11, T017 → C4–C5, T018 → C6–C7); and **C16 was added** for the GHCR package visibility the runbook never mentioned. **Outstanding**: the bootstrap step for FR-032 (see T020) and the deployment flow itself, neither of which can be written honestly until something has actually deployed.
- [X] T046 Run `/branch-review` and resolve every Required finding before `/speckit-converge` (CLAUDE.md Branch Review Gate). **Round 1 done 2026-09-23**: verdict **PASS WITH SUGGESTIONS** — zero Required findings, three Optional, all three accepted by the Product Owner and planned below as T050–T055. Re-run after they are implemented. **Round 2 done 2026-09-23**: verdict **CHANGES REQUESTED** — one Required finding (BR-004) introduced by the round-1 remediation itself, planned below as T056–T058. Re-run after they are implemented. **Round 3 done 2026-09-23**: verdict **PASS** — zero Required, zero Optional. Per CLAUDE.md a round with no Required findings is the stopping point, so the review gate is closed.

### Review remediation — round 1 (2026-09-23)

Three Optional findings from `/branch-review`, all accepted. Nothing here blocks the merge; each
closes a gap the design named but did not enforce.

**BR-001 — `/health/database` is anonymous on stage** (Medium). `contracts/health-api.md` says the
check is production-only because each call wakes stage's auto-paused free-tier database and spends
about an hour of a ~55-hour monthly allowance. That is enforced only by `deploy-stage.yml` not
calling it; the endpoint is registered unconditionally, so anything on the internet can.

- [X] T050 [US3] Add a **failing** regression test in `backend/tests/LootSingles.IntegrationTests/Health/DatabaseHealthEndpointTests.cs` proving the endpoint is exposed where it should not be: with the database health endpoint **disabled** by configuration, `GET /health/database` must return **404** while `GET /health` still returns **200** in the same host. Fails today because `Program.cs` registers the endpoint unconditionally — it currently answers 200 or 503, never 404.
- [X] T051 [US3] Gate the `/health/database` registration in `backend/src/LootSingles.Api/Program.cs` on a single configuration flag that **defaults to disabled**, so an environment must opt in. Makes T050 pass. Keep the existing 200/503 and no-detail assertions passing by enabling the flag in those tests' host configuration — they are the FR-024/FR-025 coverage and must not be weakened. Record the mechanism in `specs/019-automated-deployment/contracts/health-api.md`, whose "Why production only" section currently states the intent without the means.
- [X] T052 [US3] [MANUAL] Set the flag on **production's** container app only, leaving stage without it, and document the setting in `quickstart.md` (C9, where the app's configuration is created) and in C16's pre-deploy checks. **Do this before the first production release**: `deploy-production.yml`'s smoke test runs `check /health/database 200`, so a production app without the flag fails its own release.

**BR-002 — `inputs.commit` is interpolated into shell and never validated** (Medium). `deploy-production.yml:47–48` expands the input directly inside a `run:` block, which is the injection pattern GitHub's hardening guidance warns about, and nothing checks the value is a full SHA. `deploy-stage.yml:55` already does this correctly with `${GITHUB_SHA}`.

- [X] T053 Add a **failing** text assertion to `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs` proving `.github/workflows/deploy-production.yml` does not expand `${{ inputs.commit }}` inside a `run:` block and does carry a 40-hex-character validation of the commit. Fails today on both halves. Follow the text-assertion style T039 established.
- [X] T054 Fix `.github/workflows/deploy-production.yml` so the commit reaches the shell through `env:` as a quoted variable, matching `deploy-stage.yml`, and add a `^[0-9a-f]{40}$` guard as the **first** step so a short SHA fails before the migrate job is repointed at an image that does not exist. Makes T053 pass. **Leave the job `name:` interpolation at line 33 alone** — it is not shell, and FR-013 depends on the approver seeing the commit in the panel holding the approval button.

**BR-003 — nothing enforces additive-only migrations** (Low). FR-016 requires schema changes to stay backward-compatible so the automatic rollback in FR-017 restores a working application. The only trace of that rule is a comment.

- [X] T055 Add an assertion to `backend/tests/LootSingles.IntegrationTests/Persistence/MigrationTests.cs` that no file under `Persistence/Migrations/` contains `DropColumn`, `DropTable` or `RenameColumn`. **This guard passes the moment it is written**, because every current migration complies — that is the intended state, not a weak test. It is a guard against a future migration silently making rollback destructive, not a reproduction of a present defect, so no test-first red step applies.

### Review remediation — round 2 (2026-09-23)

One Required finding, introduced by the round-1 remediation itself.

**BR-004 — an unset flag serves the web app instead of 404** (High, Required). With
`HealthChecks:ExposeDatabaseEndpoint` unset, `/health/database` is not mapped, so
`MapFallbackToFile("index.html")` catches it and answers **200 with the web app's HTML**. Confirmed
against the real built application: flag off with a web root present → `200 <!doctype html>…`;
flag on → `503 Database unavailable.` T050 passes only because its test host has no `index.html`.

The consequence inverts the safety net T052 describes. If production ever lacks the flag — a Part E
reset, or a container app rebuilt from C9 — `deploy-production.yml`'s `check /health/database 200`
gets the SPA shell's 200 and **passes**, so FR-024's proof silently disappears and a release whose
application cannot reach its database is reported successful. Stage's database is still never woken,
so BR-001's actual aim holds; the defect is only in what an *absent* endpoint looks like.

The fix makes the runbook (C9, C16), `contracts/health-api.md` and T052 true as already written, so
**no documentation changes** — only code and tests.

- [X] T056 [US3] Make the T050 test in `backend/tests/LootSingles.IntegrationTests/Health/DatabaseHealthEndpointTests.cs` able to fail: seed a temporary web root containing an `index.html` with `id="root"` and point the host at it with `UseWebRoot`, following `Hosting/SpaFallbackTests.cs`. Keep its assertions unchanged — `/health/database` **404** and `/health` **200** in the same host. **Must fail against the current code**, returning 200 with the web app's HTML; confirm that before starting T057. A real container always has `wwwroot/index.html`, so a test host without one is testing a situation production never has.
- [X] T057 [US3] Add `app.MapFallback("/health/{**path}", () => Results.NotFound());` in `backend/src/LootSingles.Api/Program.cs`, beside the existing `/api` fallback and **before** `MapFallbackToFile("index.html")`, so an unmapped health path is a real 404 rather than the web app. Extend the comment above the `/api` fallback to say both prefixes are server-owned — it already names this exact trap ("a test asserting 404 passes for the wrong reason"). Makes T056 pass. No client-side route lives under `/health`, so nothing in the web app is shadowed.
- [X] T058 [US3] Prove the new catch-all does not shadow the explicit routes, in `backend/tests/LootSingles.IntegrationTests/Health/DatabaseHealthEndpointTests.cs`: with the flag **on** and the same seeded web root, `/health/database` returns **503** whose body is not HTML (no `id="root"`), and `/health` returns **200**. The `{**path}` catch-all can match an empty remainder, so this pins that explicit endpoints still outrank the fallback — the property the whole fix depends on. Passes on creation after T057; it guards the fix rather than reproducing the defect.

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
