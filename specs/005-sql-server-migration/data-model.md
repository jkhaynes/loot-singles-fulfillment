# Data Model: SQL Server-Only Persistence

No product entity or approved behavior is added. This feature standardizes schema realization and test lifecycle around the existing model.

## Existing persisted model

| Entity | Relevant relationships/constraints | SQL Server behavior to validate |
|--------|------------------------------------|---------------------------------|
| `Employee` | Identity key; unique normalized username | Unique enforcement, role/string storage, lockout state, creation time |
| `EmployeeAuditEvent` | Identity key; actor and optional target identifiers | `DateTimeOffset` SQL ordering and modeled index/relationship policy |
| `ImportAttempt` | Identity key; started/completed time; optional failure | Nullable offsets, transaction participation, atomic completion |
| `ImportOrderResult` | Required attempt; optional resulting order | Foreign keys and rollback with order writes |
| `Order` | Identity key; unique TCGplayer identifier; status/import time | Unique enforcement, SQL time ordering, concurrency behavior |
| `OrderLine` | Required order with cascade; card attributes/quantity | Foreign key, cascade, atomic order/line persistence |

## Schema lifecycle

```text
empty SQL Server database
  → apply checked-in migrations in order
  → current model matches applied schema
  → seed only synthetic environment/test data
  → run application or test behavior
```

- `EnsureCreated` is not supported for relational application, integration, or E2E databases.
- Existing migrations remain history; a mismatch receives a forward corrective migration.
- Development data may be discarded; no data-transfer model is required.

## Test infrastructure

### Test run

- One pinned disposable SQL Server container and a master administrative connection.
- Lifecycle: not started → starting → ready → disposing → removed.
- Startup or disposal failure remains visible to the test runner.

### Database lease

- Collision-resistant database name containing no user or secret data.
- Runtime-derived connection string, never committed or logged with credentials.
- All application migrations applied before use.
- Exactly one independent test/factory/host scope owns a lease.
- Lifecycle: allocated → created → migrated → used → connections disposed/pools cleared → dropped.

### Isolation invariants

1. A lease never uses `loot-singles-dev` or externally configured storage.
2. Two live leases never share a name.
3. Seeding follows migration completion.
4. Disposing one lease cannot alter another database.
5. Container disposal is a safety net; explicit cleanup stays observable.

## Query shapes restored to SQL Server

- Orders: project required fields, order by `ImportedAt` descending, stable identifier ascending, materialize.
- Ready dashboard: filter Ready, order by `ImportedAt` ascending with stable tie-breaker, project counts/quantities, materialize.
- Audit history: filter actor/target, order by `OccurredAt` descending with stable tie-breaker, materialize.

Tests verify results and, where necessary, generated SQL or command interception to prove server-side execution.
