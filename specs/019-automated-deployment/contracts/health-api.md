# Contract: Health endpoints

**Feature**: 019 | **Date**: 2026-09-22

Two anonymous endpoints with deliberately different jobs. The difference between them is the
contract — conflating them causes harm in both directions (research.md §4), so both are pinned by
tests.

## `GET /health`

Liveness. Answers only "is this process up and serving HTTP?"

| Aspect | Value |
|---|---|
| Authentication | None. Anonymous. |
| Database access | **None, ever.** This is the contract, not an optimisation. |
| Success | `200 OK`, empty body |
| Failure | The process is not serving; the platform sees no response |
| Callers | The container platform's probe; the post-deployment check in both environments |

**Guarantees**

- Returns `200` when the database is unreachable, paused, or misconfigured. A database problem MUST
  NOT cause a running container to be replaced (FR-023) — restarting cannot fix a database, and it
  removes an application that could still serve its sign-in page and log a useful error.
- Performs no I/O beyond answering the request.

## `GET /health/database`

Readiness against the database, for the **application's own identity**.

| Aspect | Value |
|---|---|
| Authentication | None. Anonymous. |
| Database access | One lightweight read |
| Success | `200 OK`, empty body |
| Failure | `503 Service Unavailable`, fixed body, no detail |
| Callers | **Production's post-deployment check only.** Never the container probe. |

**Guarantees**

- The failure body is a fixed string. It MUST NOT contain the connection string, the server or
  database name, the identity's client id, an exception message, or a stack trace (FR-025). The
  reason is written to `ILogger<T>` instead, where operators can reach it and callers cannot.
- Never used as a liveness or readiness probe by the platform. Wiring it to the probe would reintroduce
  exactly the failure `/health` exists to prevent.
- Proves what the migration step cannot. Since the application identity and the migration identity
  are separate (research.md §7), a successful migration says nothing about whether the *application*
  can read. FR-024 requires this proof before a production release is called successful.

**Why anonymous is acceptable**: a caller learns whether the database is reachable. That is already
observable — a broken database produces a 500 on the sign-in page. No data, count, name or
configuration value is exposed.

**Why production only**: stage's database is free-tier and auto-pauses. Each call wakes it and
consumes at least an hour of a roughly 55-hour monthly allowance. Production's is always awake, so
the check costs nothing and needs no retry loop. A broken stage database announces itself the moment
anyone opens stage.

## Routing contract (FR-004)

The web app is served from the same origin, which makes fallback order part of this contract.

| Request | Response |
|---|---|
| `GET /` | `200`, the web app's HTML |
| `GET /orders/42` (a client-side route) | `200`, the web app's HTML |
| `GET /api/unknown` | **`404`**, not HTML |
| `GET /api/orders` without a session | **`401`**, not HTML |
| `GET /assets/<hashed>.js` | `200`, the asset |

The `/api` fallback MUST be registered before the web-app fallback. Without it, an unmatched API
route returns the web app's HTML with status `200`: a client expecting JSON fails on parse rather
than on status, and a test asserting 404 passes for the wrong reason.

## Not in this contract

- No endpoint reports version, build, commit, uptime or dependency detail. Nothing in the feature
  needs it, and each would be a new anonymous information disclosure.
- No aggregate health document. Two endpoints returning `200`/`503` is the whole requirement
  (constitution Principle XIII).
