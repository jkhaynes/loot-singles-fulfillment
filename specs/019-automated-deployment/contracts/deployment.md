# Contract: Deployment workflows and the image

**Feature**: 019 | **Date**: 2026-09-22

The interfaces this feature exposes that are not HTTP: the container image's command line, and the
two deployment workflows.

## Image command line

One image serves every purpose. `ENTRYPOINT` is `dotnet LootSingles.Api.dll` with **no `CMD`**, so
the argument selects the behaviour.

| Arguments | Behaviour | Exit |
|---|---|---|
| *(none)* | Serves HTTP on port 8080 | Runs until stopped |
| `migrate` | Applies pending EF Core migrations, logs their names, returns | `0` success, `1` failure |
| `bootstrap-admin` | Creates the first manager account (existing behaviour, unchanged) | `0` success, `1` failure |

**Guarantees**

- `migrate` is **idempotent**: running it against an up-to-date database succeeds and applies
  nothing.
- No command echoes the connection string, the identity client id, or any credential — on success or
  on failure (FR-019, FR-022). The existing `DatabaseConfigurationTests` establishes this expectation
  for the missing-connection-string case; `migrate` extends it.
- `migrate` dispatches before the HTTP pipeline is built, mirroring the existing `bootstrap-admin`
  branch in `Program.cs`. It never starts a listener.
- The image runs as a **non-root** user and contains no secret. The registry is public, so anything
  baked into the image is public.

## `deploy-stage.yml`

| Aspect | Value |
|---|---|
| Trigger | Push to `main`; manual dispatch |
| Approval | None |
| Concurrency | Group `deploy-stage`, `cancel-in-progress: false` |
| Environment | `stage`, branch-restricted to `main` |

**Sequence**: quality gate against the merge commit → build and push `sha-<commit>` → **record the
running image** → run the migrate job and wait → update the Container App → post-deployment check →
**on any failure, redeploy the recorded image**.

The image is recorded *before* the migration, so a failure anywhere after that point has something to
restore. Reactivating a previous revision would be simpler but does not work in single-revision mode
(research.md §12).

**Guarantees**

- No manual action occurs between the merge and stage running that commit (FR-008, SC-002).
- The quality gate runs against the merged result, which no pull request tested (FR-010).
- Migration completes before the new version serves traffic (FR-015).

## `deploy-production.yml`

| Aspect | Value |
|---|---|
| Trigger | Manual dispatch only |
| Input | `commit` — **required, no default** |
| Approval | Required reviewer; self-approval permitted |
| Concurrency | Group `deploy-production`, `cancel-in-progress: false` |
| Environment | `production`, branch-restricted to `main` |
| Job name | Interpolates `commit`, e.g. `Deploy 83601e5 to production` |

**Sequence**: the single job is gated, so **nothing runs until approval**. Then: run the migrate job
and wait → update the Container App → post-deployment check including `/health/database` → on any
failure, redeploy the image recorded before the migration began (research.md §12 — reactivating a
previous revision does not work in single-revision mode).

**Guarantees**

- Merging code never changes production (FR-011, SC-003).
- The `commit` input is a literal value fixed when the run starts. A merge to `main` while the run
  waits for approval cannot change what ships (FR-013, SC-004).
- The approver sees the commit in the job list, which is the panel holding the *Review deployments*
  button (FR-013).
- Production runs `ghcr.io/…:sha-<commit>` — the artifact already built for that commit. Nothing is
  rebuilt (FR-014).
- Declining or never approving leaves production untouched (FR-012).

**Why the input has no default**: a default would let the workflow run without anyone naming a
version, which is the approve-a-lookup failure this design exists to prevent (research.md §10). A
text assertion pins it.

## Post-deployment check

| Check | Stage | Production | Proves |
|---|---|---|---|
| `GET /health` → 200 | ✓ | ✓ | The process started and serves HTTP |
| `GET /api/unknown` → 404 | ✓ | ✓ | Fallback ordering; an API route is not returning HTML |
| `GET /api/orders` → 401 | ✓ | ✓ | Authentication is wired and the endpoint is protected |
| `GET /` → HTML containing `id="root"` | ✓ | ✓ | The web build was copied into the image |
| `GET /health/database` → 200 | — | ✓ | The **application's** identity reaches the database (FR-024) |

Stage omits the last check deliberately; the asymmetry is explained in `contracts/health-api.md` and
pinned by a text assertion so it is not "fixed" into symmetry later.

## Secrets contract

**There are none.** Entra-only authentication means the connection string carries no password:

```
Server=…;Authentication=Active Directory Managed Identity;User Id=<clientId>;Database=…
```

It is an ordinary environment variable, not a secret — no Key Vault, no GitHub secret, nothing to
rotate. Azure sign-in uses OIDC federated credentials scoped per environment resource group, so
stage's credential cannot reach production (FR-006). Each GitHub environment holds plain variables:
subscription, tenant and client ids, resource group, app and job names, and the public URL.

The only credential-shaped value in the whole design is the bootstrap PIN, which exists for one job
run and is changed at first sign-in (FR-032).
